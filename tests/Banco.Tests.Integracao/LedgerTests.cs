using System.Globalization;
using Banco.Dominio.Contas;
using Banco.Dominio.Transferencias;
using Banco.Dominio.Usuarios;
using Banco.Infra.Persistencia;
using Banco.Infra.Transferencias;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Banco.Tests.Integracao;

/// <summary>Prova, em PostgreSQL real, que o dinheiro não some, não duplica e não fica negativo, mesmo sob concorrência.</summary>
public class LedgerTests(LedgerTests.BancoDoLedger fixture) : IClassFixture<LedgerTests.BancoDoLedger>
{
    private const string IntegrityViolation = "23000";
    private const string CheckViolation = "23514";

    public sealed class BancoDoLedger : IAsyncLifetime
    {
        public string Conexao { get; private set; } = "";
        public Guid SistemaId { get; private set; }

        public async Task InitializeAsync()
        {
            Conexao = await ServidorPostgres.CriarBancoAsync();
            var sistema = Conta.AbrirDoSistema("000001", DateTimeOffset.UtcNow);
            await using var banco = ServidorPostgres.Contexto(Conexao);
            banco.Contas.Add(sistema);
            await banco.SaveChangesAsync();
            SistemaId = sistema.Id;
        }

        public Task DisposeAsync() => Task.CompletedTask;
    }

    private static int _sequencia;

    private static ExecutorDeTransferencia NovoExecutor(string conexao) =>
        new(ServidorPostgres.Opcoes(conexao), TimeProvider.System);

    /// <summary>Cria usuário e conta de cliente. O saldo entra pelo ledger (crédito inicial contra a conta de sistema).</summary>
    private async Task<Guid> ContaComSaldoAsync(long centavos)
    {
        var n = Interlocked.Increment(ref _sequencia);
        var agora = DateTimeOffset.UtcNow;
        var usuario = Usuario.Cadastrar("Ana Souza", n.ToString("D11", CultureInfo.InvariantCulture), $"u{n}@exemplo.com", "+5511987654321", "hash", agora);
        var conta = Conta.AbrirParaCliente(usuario.Id, (1000 + n).ToString(CultureInfo.InvariantCulture), agora);

        await using var banco = ServidorPostgres.Contexto(fixture.Conexao);
        banco.Usuarios.Add(usuario);
        banco.Contas.Add(conta);
        await banco.SaveChangesAsync();

        if (centavos > 0)
        {
            var sistema = await banco.Contas.SingleAsync(c => c.Id == fixture.SistemaId);
            var valor = Dinheiro.DeCentavos(centavos);
            var credito = Transferencia.CreditoInicial(sistema.Id, conta.Id, valor, agora);
            sistema.Debitar(valor);
            conta.Creditar(valor);
            banco.Transferencias.Add(credito);
            banco.Lancamentos.AddRange(
                Lancamento.Registrar(credito.Id, sistema.Id, TipoLancamento.Debito, centavos, sistema.SaldoCentavos, agora),
                Lancamento.Registrar(credito.Id, conta.Id, TipoLancamento.Credito, centavos, conta.SaldoCentavos, agora));
            await banco.SaveChangesAsync();
        }

        return conta.Id;
    }

    /// <summary>Cria a transferência já confirmada (PROCESSANDO), como a API deixará depois da biometria ou senha.</summary>
    private async Task<Guid> ConfirmadaAsync(Guid origem, Guid destino, long centavos) =>
        (await ConfirmadasAsync([(origem, destino, centavos)])).Single();

    /// <summary>Em lote: um round-trip para criar e outro para confirmar, senão a preparação domina o tempo do teste.</summary>
    private async Task<List<Guid>> ConfirmadasAsync(IEnumerable<(Guid Origem, Guid Destino, long Centavos)> itens)
    {
        await using var banco = ServidorPostgres.Contexto(fixture.Conexao);
        var todas = itens
            .Select(i => Transferencia.Criar(i.Origem, i.Destino, Dinheiro.DeCentavos(i.Centavos), null, DateTimeOffset.UtcNow))
            .ToList();
        banco.Transferencias.AddRange(todas);
        await banco.SaveChangesAsync();
        todas.ForEach(t => t.Confirmar(MetodoConfirmacao.Senha));
        await banco.SaveChangesAsync();
        return todas.Select(t => t.Id).ToList();
    }

    private async Task<long> SaldoAsync(Guid contaId)
    {
        await using var banco = ServidorPostgres.Contexto(fixture.Conexao);
        return await banco.Contas.Where(c => c.Id == contaId).Select(c => c.SaldoCentavos).SingleAsync();
    }

    /// <summary>As três conferências de conciliação: devem devolver zero ao fim de qualquer cenário.</summary>
    private async Task ConciliarAsync()
    {
        await using var banco = ServidorPostgres.Contexto(fixture.Conexao);

        var desbalanceadas = await banco.Database.SqlQueryRaw<long>(
            "SELECT count(*) AS \"Value\" FROM (SELECT 1 FROM lancamentos GROUP BY transferencia_id " +
            "HAVING sum(CASE tipo WHEN 'D' THEN valor_centavos ELSE -valor_centavos END) <> 0) x").SingleAsync();
        var saldosDivergentes = await banco.Database.SqlQueryRaw<long>(
            "SELECT count(*) AS \"Value\" FROM contas c WHERE c.saldo_centavos <> coalesce(" +
            "(SELECT sum(CASE tipo WHEN 'D' THEN -valor_centavos ELSE valor_centavos END) FROM lancamentos WHERE conta_id = c.id), 0)").SingleAsync();
        var somaDosSaldos = await banco.Database.SqlQueryRaw<long>(
            "SELECT coalesce(sum(saldo_centavos), 0)::bigint AS \"Value\" FROM contas").SingleAsync();

        Assert.Equal(0, desbalanceadas);
        Assert.Equal(0, saldosDivergentes);
        Assert.Equal(0, somaDosSaldos);
    }

    [Fact]
    public async Task Executar_move_o_dinheiro_e_grava_as_duas_pernas()
    {
        var a = await ContaComSaldoAsync(10_000);
        var b = await ContaComSaldoAsync(0);
        var id = await ConfirmadaAsync(a, b, 2_500);

        var status = await NovoExecutor(fixture.Conexao).ExecutarAsync(id);

        Assert.Equal(StatusTransferencia.Concluida, status);
        Assert.Equal(7_500, await SaldoAsync(a));
        Assert.Equal(2_500, await SaldoAsync(b));
        await using var banco = ServidorPostgres.Contexto(fixture.Conexao);
        var pernas = await banco.Lancamentos.Where(l => l.TransferenciaId == id).ToListAsync();
        Assert.Equal(7_500, pernas.Single(l => l.Tipo == TipoLancamento.Debito).SaldoAposCentavos);
        Assert.Equal(2_500, pernas.Single(l => l.Tipo == TipoLancamento.Credito).SaldoAposCentavos);
        await ConciliarAsync();
    }

    [Fact]
    public async Task Saldo_insuficiente_falha_sem_mover_dinheiro()
    {
        var a = await ContaComSaldoAsync(1_000);
        var b = await ContaComSaldoAsync(0);
        var id = await ConfirmadaAsync(a, b, 1_001);

        var status = await NovoExecutor(fixture.Conexao).ExecutarAsync(id);

        Assert.Equal(StatusTransferencia.Falha, status);
        Assert.Equal(1_000, await SaldoAsync(a));
        await using var banco = ServidorPostgres.Contexto(fixture.Conexao);
        var t = await banco.Transferencias.SingleAsync(x => x.Id == id);
        Assert.Equal(MotivoFalha.SaldoInsuficiente, t.MotivoFalha);
        Assert.Empty(await banco.Lancamentos.Where(l => l.TransferenciaId == id).ToListAsync());
        await ConciliarAsync();
    }

    [Fact]
    public async Task Executar_duas_vezes_nao_move_o_dinheiro_duas_vezes()
    {
        var a = await ContaComSaldoAsync(10_000);
        var b = await ContaComSaldoAsync(0);
        var id = await ConfirmadaAsync(a, b, 4_000);
        var executor = NovoExecutor(fixture.Conexao);

        await executor.ExecutarAsync(id);
        var segunda = await executor.ExecutarAsync(id);

        Assert.Equal(StatusTransferencia.Concluida, segunda);
        Assert.Equal(6_000, await SaldoAsync(a));
        await ConciliarAsync();
    }

    [Fact]
    public async Task Banco_registra_o_historico_e_recusa_transicao_invalida()
    {
        var a = await ContaComSaldoAsync(5_000);
        var b = await ContaComSaldoAsync(0);
        var id = await ConfirmadaAsync(a, b, 1_000);
        await NovoExecutor(fixture.Conexao).ExecutarAsync(id);
        await using var banco = ServidorPostgres.Contexto(fixture.Conexao);

        var historico = await banco.Database.SqlQuery<string>(
            $"SELECT coalesce(de_status, '') || '>' || para_status AS \"Value\" FROM transferencia_historico WHERE transferencia_id = {id} ORDER BY id").ToListAsync();
        var erro = await Assert.ThrowsAsync<PostgresException>(
            () => banco.Database.ExecuteSqlAsync($"UPDATE transferencias SET status = 'PROCESSANDO' WHERE id = {id}"));

        Assert.Equal([">PENDENTE_BIOMETRIA", "PENDENTE_BIOMETRIA>PROCESSANDO", "PROCESSANDO>CONCLUIDA"], historico);
        Assert.Equal(CheckViolation, erro.SqlState);
    }

    [Fact]
    public async Task Lancamento_nao_pode_ser_alterado_nem_apagado()
    {
        var a = await ContaComSaldoAsync(5_000);
        var b = await ContaComSaldoAsync(0);
        await NovoExecutor(fixture.Conexao).ExecutarAsync(await ConfirmadaAsync(a, b, 1_000));
        await using var banco = ServidorPostgres.Contexto(fixture.Conexao);

        var alterar = await Assert.ThrowsAsync<PostgresException>(
            () => banco.Database.ExecuteSqlRawAsync("UPDATE lancamentos SET valor_centavos = valor_centavos + 1 WHERE conta_id = (SELECT id FROM contas LIMIT 1)"));
        var apagar = await Assert.ThrowsAsync<PostgresException>(() => banco.Database.ExecuteSqlRawAsync("DELETE FROM lancamentos"));
        var truncar = await Assert.ThrowsAsync<PostgresException>(() => banco.Database.ExecuteSqlRawAsync("TRUNCATE lancamentos CASCADE"));

        Assert.All([alterar, apagar, truncar], e => Assert.Equal(IntegrityViolation, e.SqlState));
        await ConciliarAsync();
    }

    [Fact]
    public async Task Ledger_desbalanceado_e_recusado_no_commit_e_nada_fica_gravado()
    {
        var a = await ContaComSaldoAsync(5_000);
        var b = await ContaComSaldoAsync(0);
        var id = await ConfirmadaAsync(a, b, 1_000);

        await using (var banco = ServidorPostgres.Contexto(fixture.Conexao))
        await using (var transacao = await banco.Database.BeginTransactionAsync())
        {
            // Só a perna de débito: o dinheiro sairia sem entrar em lugar nenhum.
            banco.Lancamentos.Add(Lancamento.Registrar(id, a, TipoLancamento.Debito, 1_000, 4_000, DateTimeOffset.UtcNow));
            await banco.SaveChangesAsync();

            var erro = await Assert.ThrowsAsync<PostgresException>(() => transacao.CommitAsync());
            Assert.Equal(IntegrityViolation, erro.SqlState);
        }

        await using var leitura = ServidorPostgres.Contexto(fixture.Conexao);
        Assert.Empty(await leitura.Lancamentos.Where(l => l.TransferenciaId == id).ToListAsync());
        Assert.Equal(5_000, await SaldoAsync(a));
        await ConciliarAsync();
    }

    [Fact]
    public async Task Cinquenta_transferencias_de_R10_contra_saldo_de_R100_concluem_dez()
    {
        var a = await ContaComSaldoAsync(10_000);
        var b = await ContaComSaldoAsync(0);
        var ids = await ConfirmadasAsync(Enumerable.Repeat((a, b, 1_000L), 50));

        var resultados = await ExecutarEmParaleloAsync(ids);

        Assert.Equal(10, resultados.Count(s => s == StatusTransferencia.Concluida));
        Assert.Equal(40, resultados.Count(s => s == StatusTransferencia.Falha));
        Assert.Equal(0, await SaldoAsync(a));
        Assert.Equal(10_000, await SaldoAsync(b));
        await ConciliarAsync();
    }

    [Fact]
    public async Task Transferencias_cruzadas_de_A_para_B_e_de_B_para_A_nao_travam_nem_perdem_dinheiro()
    {
        var a = await ContaComSaldoAsync(100_000);
        var b = await ContaComSaldoAsync(100_000);
        var ids = await ConfirmadasAsync(Enumerable.Range(0, 200).SelectMany(_ => new[] { (a, b, 100L), (b, a, 100L) }));

        var resultados = await ExecutarEmParaleloAsync(ids);

        Assert.All(resultados, s => Assert.Equal(StatusTransferencia.Concluida, s));
        Assert.Equal(200_000, await SaldoAsync(a) + await SaldoAsync(b));
        await ConciliarAsync();
    }

    private async Task<StatusTransferencia[]> ExecutarEmParaleloAsync(List<Guid> ids)
    {
        var resultados = new StatusTransferencia[ids.Count];
        await Parallel.ForEachAsync(
            Enumerable.Range(0, ids.Count),
            new ParallelOptions { MaxDegreeOfParallelism = 20 },
            async (i, cancelamento) => resultados[i] = await NovoExecutor(fixture.Conexao).ExecutarAsync(ids[i], cancelamento));
        return resultados;
    }
}
