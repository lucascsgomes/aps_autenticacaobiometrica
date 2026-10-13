using System.Security.Cryptography;
using Banco.Dominio;
using Banco.Dominio.Chaves;
using Banco.Dominio.Contas;
using Banco.Dominio.Transferencias;
using Banco.Dominio.Usuarios;
using Banco.Dominio.Biometria;
using Banco.Infra.Autenticacao;
using Banco.Infra.Facial;
using Banco.Infra.Persistencia;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Banco.Infra.Cadastro;

public sealed record NovoCadastro(string Nome, string Cpf, string Email, string? Celular, string Senha, string VersaoTermoBiometria);

public sealed record CadastroCriado(Guid UsuarioId, string Nome, Guid ContaId, string Agencia, string Numero, StatusUsuario Status, string? RefreshToken);

public sealed record CadastroRetomado(Guid UsuarioId, string Nome, Guid ContaId, string Agencia, string Numero);

public sealed class ServicoDeCadastro(BancoDbContext banco, ServicoDeSenha senhas, RefreshTokenServico refresh, CifraDeTemplate cifra, TimeProvider relogio)
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

    /// <summary>
    /// Guarda o rosto cifrado e conclui o cadastro: ativa o usuário, concede o crédito inicial pelo ledger e abre a sessão, tudo numa transação.
    /// A linha do usuário é travada, então duas fotos simultâneas do mesmo cadastro não concluem duas vezes.
    /// </summary>
    public async Task<SessaoEmitida> ConcluirComRostoAsync(Guid usuarioId, byte[] embedding, CancellationToken cancelamento = default)
    {
        var agora = relogio.GetUtcNow();
        banco.ChangeTracker.Clear();
        await using var transacao = await banco.Database.BeginTransactionAsync(cancelamento);

        var usuario = await banco.Usuarios
            .FromSql($"SELECT * FROM usuarios WHERE id = {usuarioId} FOR UPDATE")
            .SingleOrDefaultAsync(cancelamento);
        if (usuario is not { Status: StatusUsuario.CadastroPendente })
        {
            throw new TokenDeCadastroEncerradoException();
        }

        var conta = await banco.Contas.SingleAsync(c => c.UsuarioId == usuarioId, cancelamento);
        var (cifrado, nonce) = cifra.Cifrar(usuarioId, embedding);
        banco.Templates.Add(TemplateFacial.Criar(usuarioId, cifrado, nonce, cifra.Versao, agora));
        usuario.ConcluirCadastro(agora);
        await ConcederCreditoInicialAsync(usuario, conta, agora, cancelamento);
        var refreshToken = refresh.NovaFamilia(usuarioId);

        await banco.SaveChangesAsync(cancelamento);
        await transacao.CommitAsync(cancelamento);
        return new SessaoEmitida(usuarioId, usuario.Nome, conta.Id, refreshToken);
    }

    /// <summary>
    /// Prova a posse de um cadastro pendente pelo CPF e pela senha informados no cadastro. CPF inexistente, cadastro que não
    /// está pendente e senha errada respondem igual (e contam para o bloqueio); só quem acerta a senha descobre que o cadastro já foi concluído.
    /// </summary>
    public async Task<CadastroRetomado> ProvarPosseAsync(string cpf, string senha, CancellationToken cancelamento = default)
    {
        var agora = relogio.GetUtcNow();
        var usuario = await banco.Usuarios.SingleOrDefaultAsync(u => u.Cpf == cpf, cancelamento);
        var senhaCorreta = senhas.Verificar(usuario?.SenhaHash, senha);

        if (usuario is null || usuario.Status == StatusUsuario.Bloqueado || usuario.Status == StatusUsuario.Encerrado)
        {
            throw new CredenciaisInvalidasException();
        }

        if (!senhaCorreta)
        {
            if (!usuario.EstaBloqueado(agora))
            {
                usuario.RegistrarFalhaDeCredencial(agora);
                await banco.SaveChangesAsync(cancelamento);
            }

            throw new CredenciaisInvalidasException();
        }

        if (usuario.EstaBloqueado(agora))
        {
            throw new ContaBloqueadaException(usuario.BloqueadoAte!.Value - agora);
        }

        if (usuario.Status != StatusUsuario.CadastroPendente)
        {
            throw new ConflitoDeCadastroException();
        }

        var conta = await banco.Contas.AsNoTracking().SingleAsync(c => c.UsuarioId == usuario.Id, cancelamento);
        return new CadastroRetomado(usuario.Id, usuario.Nome, conta.Id, conta.Agencia, conta.Numero);
    }

    /// <summary>Sobrescreve os dados do cadastro pendente (mesmo usuário e mesma conta). Quem chama já provou a posse.</summary>
    public async Task RecomecarAsync(Guid usuarioId, NovoCadastro dados, CancellationToken cancelamento = default)
    {
        var senhaHash = senhas.Hash(dados.Senha);
        var usuario = await banco.Usuarios.SingleAsync(u => u.Id == usuarioId, cancelamento);
        usuario.ReiniciarCadastro(dados.Nome, dados.Email, dados.Celular, senhaHash, dados.VersaoTermoBiometria, relogio.GetUtcNow());
        try
        {
            await banco.SaveChangesAsync(cancelamento);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: ViolacaoDeUnicidade })
        {
            throw new ConflitoDeCadastroException();
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
        banco.Chaves.Add(ChaveTransferencia.Criar(conta.Id, TipoChave.Cpf, usuario.Cpf, agora));

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
