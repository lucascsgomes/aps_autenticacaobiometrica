using Banco.Dominio.Contas;
using Banco.Dominio.Transferencias;
using Banco.Infra.Persistencia;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Banco.Infra.Transferencias;

/// <summary>
/// Move o dinheiro de uma transferência já confirmada (PROCESSANDO) numa única transação:
/// trava a transferência e as duas contas em ordem fixa de id, debita, credita e grava as duas pernas do ledger.
/// </summary>
public sealed class ExecutorDeTransferencia(DbContextOptions<BancoDbContext> opcoes, TimeProvider relogio)
{
    private const string DeadlockDetectado = "40P01";
    private const string FalhaDeSerializacao = "40001";

    /// <summary>Devolve o status final. Chamar de novo para uma transferência já resolvida não move dinheiro outra vez.</summary>
    public async Task<StatusTransferencia> ExecutarAsync(Guid transferenciaId, CancellationToken cancelamento = default)
    {
        try
        {
            return await TentarAsync(transferenciaId, cancelamento);
        }
        catch (Exception e) when (ConflitoDeConcorrencia(e))
        {
            // Uma repetição da transação inteira basta: o Postgres desfez a primeira por completo.
            return await TentarAsync(transferenciaId, cancelamento);
        }
    }

    private async Task<StatusTransferencia> TentarAsync(Guid transferenciaId, CancellationToken cancelamento)
    {
        await using var banco = new BancoDbContext(opcoes);
        await using var transacao = await banco.Database.BeginTransactionAsync(cancelamento);

        var transferencia = await banco.Transferencias
            .FromSql($"SELECT * FROM transferencias WHERE id = {transferenciaId} FOR UPDATE")
            .SingleAsync(cancelamento);
        if (transferencia.Status != StatusTransferencia.Processando)
        {
            return transferencia.Status;
        }

        // ORDER BY id é o que impede deadlock entre A para B e B para A.
        var contas = await banco.Contas
            .FromSql($"SELECT * FROM contas WHERE id IN ({transferencia.ContaOrigemId}, {transferencia.ContaDestinoId}) ORDER BY id FOR UPDATE")
            .ToListAsync(cancelamento);
        var origem = contas.Single(c => c.Id == transferencia.ContaOrigemId);
        var destino = contas.Single(c => c.Id == transferencia.ContaDestinoId);
        var valor = transferencia.Valor;
        var agora = relogio.GetUtcNow();

        if (destino.Status != Dominio.Contas.StatusConta.Ativa)
        {
            transferencia.Falhar(MotivoFalha.DestinoIndisponivel);
        }
        else if (!origem.PodeDebitar(valor))
        {
            transferencia.Falhar(MotivoFalha.SaldoInsuficiente);
        }
        else if (origem.Tipo == TipoConta.Cliente && await MotivoDeLimiteAsync(banco, origem, agora, valor.Centavos, cancelamento) is { } motivoDoLimite)
        {
            transferencia.Falhar(motivoDoLimite);
        }
        else
        {
            origem.Debitar(valor);
            destino.Creditar(valor);
            banco.Lancamentos.AddRange(
                Lancamento.Registrar(transferencia.Id, origem.Id, TipoLancamento.Debito, valor.Centavos, origem.SaldoCentavos, agora),
                Lancamento.Registrar(transferencia.Id, destino.Id, TipoLancamento.Credito, valor.Centavos, destino.SaldoCentavos, agora));
            transferencia.Concluir(agora);
        }

        await banco.SaveChangesAsync(cancelamento);
        await transacao.CommitAsync(cancelamento);
        return transferencia.Status;
    }

    // Roda com a linha da conta de origem travada: duas transferências simultâneas não passam juntas pelo limite.
    private static async Task<string?> MotivoDeLimiteAsync(BancoDbContext banco, Conta origem, DateTimeOffset agora, long centavos, CancellationToken cancelamento)
    {
        var janelas = PoliticaDeLimite.Calcular(origem, agora);
        var (dia, noite) = await ConsumoDeLimite.SomarAsync(banco, origem.Id, janelas, cancelamento);
        return PoliticaDeLimite.MotivoDaRecusa(origem, janelas, dia, noite, centavos);
    }

    private static bool ConflitoDeConcorrencia(Exception e) =>
        (e as PostgresException ?? e.InnerException as PostgresException)?.SqlState is DeadlockDetectado or FalhaDeSerializacao;
}
