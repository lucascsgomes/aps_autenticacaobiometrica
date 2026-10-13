using System.Security.Cryptography;
using Banco.Dominio;
using Banco.Dominio.Contas;
using Banco.Dominio.Transferencias;
using Banco.Dominio.Usuarios;
using Banco.Infra.Autenticacao;
using Banco.Infra.Persistencia;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Banco.Infra.Cadastro;

public sealed record NovoCadastro(string Nome, string Cpf, string Email, string? Celular, string Senha, string VersaoTermoBiometria);

public sealed record CadastroCriado(Guid UsuarioId, string Nome, Guid ContaId, string Agencia, string Numero, StatusUsuario Status, string? RefreshToken);

public sealed class ServicoDeCadastro(BancoDbContext banco, ServicoDeSenha senhas, RefreshTokenServico refresh, TimeProvider relogio)
{
    public static readonly Dinheiro CreditoInicial = Dinheiro.DeReais(5_000);

    private const string ViolacaoDeUnicidade = "23505";
    private const int TentativasDeNumeroDeConta = 5;

    /// <summary>
    /// Cria usuário e conta de cliente em CADASTRO_PENDENTE. Com <paramref name="concluirSemRosto"/> (só em Development)
    /// já ativa o usuário, concede o crédito inicial pelo ledger e abre a sessão; sem ele, a conta só ativa quando o rosto for cadastrado.
    /// </summary>
    public async Task<CadastroCriado> CadastrarAsync(NovoCadastro dados, bool concluirSemRosto, CancellationToken cancelamento = default)
    {
        var senhaHash = senhas.Hash(dados.Senha);

        for (var tentativa = 1; ; tentativa++)
        {
            banco.ChangeTracker.Clear();
            try
            {
                return await TentarAsync(dados, senhaHash, concluirSemRosto, cancelamento);
            }
            catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: ViolacaoDeUnicidade } pg)
            {
                // Só o número aleatório da conta pode colidir sem ser culpa do cliente: sorteia outro.
                if (pg.ConstraintName != "uq_contas_numero" || tentativa == TentativasDeNumeroDeConta)
                {
                    throw new ConflitoDeCadastroException();
                }
            }
        }
    }

    private async Task<CadastroCriado> TentarAsync(NovoCadastro dados, string senhaHash, bool concluirSemRosto, CancellationToken cancelamento)
    {
        var agora = relogio.GetUtcNow();
        var usuario = Usuario.Cadastrar(dados.Nome, dados.Cpf, dados.Email, dados.Celular, senhaHash, agora);
        usuario.RegistrarConsentimentoBiometrico(dados.VersaoTermoBiometria, agora);
        var conta = Conta.AbrirParaCliente(usuario.Id, NovoNumeroDeConta(), agora);
        string? refreshToken = null;

        await using var transacao = await banco.Database.BeginTransactionAsync(cancelamento);
        banco.Usuarios.Add(usuario);
        banco.Contas.Add(conta);

        if (concluirSemRosto)
        {
            usuario.ConcluirCadastro(agora);
            await ConcederCreditoInicialAsync(usuario, conta, agora, cancelamento);
            refreshToken = refresh.NovaFamilia(usuario.Id);
        }

        await banco.SaveChangesAsync(cancelamento);
        await transacao.CommitAsync(cancelamento);
        return new CadastroCriado(usuario.Id, usuario.Nome, conta.Id, conta.Agencia, conta.Numero, usuario.Status, refreshToken);
    }

    /// <summary>Dinheiro fictício sai da conta de sistema: o ledger continua balanceado e a soma de todos os saldos continua zero.</summary>
    private async Task ConcederCreditoInicialAsync(Usuario usuario, Conta destino, DateTimeOffset agora, CancellationToken cancelamento)
    {
        var sistema = await TravarContaDoSistemaAsync(agora, cancelamento);
        var credito = Transferencia.CreditoInicial(sistema.Id, destino.Id, CreditoInicial, agora);
        sistema.Debitar(CreditoInicial);
        destino.Creditar(CreditoInicial);
        banco.Transferencias.Add(credito);
        banco.Lancamentos.AddRange(
            Lancamento.Registrar(credito.Id, sistema.Id, TipoLancamento.Debito, CreditoInicial.Centavos, sistema.SaldoCentavos, agora),
            Lancamento.Registrar(credito.Id, destino.Id, TipoLancamento.Credito, CreditoInicial.Centavos, destino.SaldoCentavos, agora));
    }

    /// <summary>Cria a conta de sistema se ainda não existe e trava a linha, senão dois cadastros simultâneos perderiam um débito.</summary>
    private async Task<Conta> TravarContaDoSistemaAsync(DateTimeOffset agora, CancellationToken cancelamento)
    {
        var nova = Conta.AbrirDoSistema("000001", agora);
        await banco.Database.ExecuteSqlAsync($"""
            INSERT INTO contas (id, tipo, agencia, numero, saldo_centavos, limite_diurno_centavos, limite_noturno_centavos,
                                inicio_noturno, fim_noturno, status, criado_em)
            VALUES ({nova.Id}, 'SISTEMA', '0001', '000001', 0, 0, 0, '20:00', '06:00', 'ATIVA', {agora})
            ON CONFLICT DO NOTHING
            """, cancelamento);
        return await banco.Contas
            .FromSql($"SELECT * FROM contas WHERE tipo = 'SISTEMA' FOR UPDATE")
            .SingleAsync(cancelamento);
    }

    private static string NovoNumeroDeConta() =>
        RandomNumberGenerator.GetInt32(100_000, 1_000_000).ToString(System.Globalization.CultureInfo.InvariantCulture);
}
