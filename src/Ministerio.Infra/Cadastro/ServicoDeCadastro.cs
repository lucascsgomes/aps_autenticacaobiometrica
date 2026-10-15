using Ministerio.Dominio;
using Ministerio.Dominio.Biometria;
using Ministerio.Dominio.Usuarios;
using Ministerio.Infra.Autenticacao;
using Ministerio.Infra.Facial;
using Ministerio.Infra.Persistencia;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Ministerio.Infra.Cadastro;

public sealed record NovoCadastro(string Nome, string Cpf, string Email, string? Celular, string Senha, string VersaoTermoBiometria);

public sealed record CadastroCriado(Guid UsuarioId, string Nome, StatusUsuario Status, string? RefreshToken);

public sealed record CadastroRetomado(Guid UsuarioId, string Nome);

public sealed class ServicoDeCadastro(MinisterioDbContext db, ServicoDeSenha senhas, RefreshTokenServico refresh, CifraDeTemplate cifra, TimeProvider relogio)
{
    private const string ViolacaoDeUnicidade = "23505";

    /// <summary>
    /// Cria o usuário em CADASTRO_PENDENTE. Com <paramref name="concluirSemRosto"/> (só em Development) já ativa o usuário e abre a sessão;
    /// sem ele, o cadastro só termina quando o rosto for cadastrado.
    /// </summary>
    public async Task<CadastroCriado> CadastrarAsync(NovoCadastro dados, bool concluirSemRosto, CancellationToken cancelamento = default)
    {
        var agora = relogio.GetUtcNow();
        var usuario = Usuario.Cadastrar(dados.Nome, dados.Cpf, dados.Email, dados.Celular, senhas.Hash(dados.Senha), agora);
        usuario.RegistrarConsentimentoBiometrico(dados.VersaoTermoBiometria, agora);
        string? refreshToken = null;
        if (concluirSemRosto)
        {
            usuario.ConcluirCadastro(agora);
            refreshToken = refresh.NovaFamilia(usuario.Id);
        }

        db.Usuarios.Add(usuario);
        try
        {
            await db.SaveChangesAsync(cancelamento);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: ViolacaoDeUnicidade })
        {
            throw new ConflitoDeCadastroException();
        }

        return new CadastroCriado(usuario.Id, usuario.Nome, usuario.Status, refreshToken);
    }

    /// <summary>
    /// Guarda o rosto cifrado e conclui o cadastro: ativa o usuário e abre a sessão, tudo numa transação.
    /// A linha do usuário é travada, então duas fotos simultâneas do mesmo cadastro não concluem duas vezes.
    /// </summary>
    public async Task<SessaoEmitida> ConcluirComRostoAsync(Guid usuarioId, byte[] embedding, CancellationToken cancelamento = default)
    {
        var agora = relogio.GetUtcNow();
        db.ChangeTracker.Clear();
        await using var transacao = await db.Database.BeginTransactionAsync(cancelamento);

        var usuario = await db.Usuarios
            .FromSql($"SELECT * FROM usuarios WHERE id = {usuarioId} FOR UPDATE")
            .SingleOrDefaultAsync(cancelamento);
        if (usuario is not { Status: StatusUsuario.CadastroPendente })
        {
            throw new TokenDeCadastroEncerradoException();
        }

        var (cifrado, nonce) = cifra.Cifrar(usuarioId, embedding);
        db.Templates.Add(TemplateFacial.Criar(usuarioId, cifrado, nonce, cifra.Versao, agora));
        usuario.ConcluirCadastro(agora);
        var refreshToken = refresh.NovaFamilia(usuarioId);

        await db.SaveChangesAsync(cancelamento);
        await transacao.CommitAsync(cancelamento);
        return new SessaoEmitida(usuarioId, usuario.Nome, usuario.Cpf, usuario.Nivel, usuario.Admin, refreshToken);
    }

    /// <summary>
    /// Prova a posse de um cadastro pendente pelo CPF e pela senha informados no cadastro. CPF inexistente, cadastro que não
    /// está pendente e senha errada respondem igual (e contam para o bloqueio); só quem acerta a senha descobre que o cadastro já foi concluído.
    /// </summary>
    public async Task<CadastroRetomado> ProvarPosseAsync(string cpf, string senha, CancellationToken cancelamento = default)
    {
        var agora = relogio.GetUtcNow();
        var usuario = await db.Usuarios.SingleOrDefaultAsync(u => u.Cpf == cpf, cancelamento);
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
                await db.SaveChangesAsync(cancelamento);
            }

            throw new CredenciaisInvalidasException();
        }

        if (usuario.EstaBloqueado(agora))
        {
            throw new ContaBloqueadaException(usuario.BloqueadoAte!.Value - agora);
        }

        return usuario.Status != StatusUsuario.CadastroPendente
            ? throw new ConflitoDeCadastroException()
            : new CadastroRetomado(usuario.Id, usuario.Nome);
    }

    /// <summary>Sobrescreve os dados do cadastro pendente (mesmo usuário). Quem chama já provou a posse.</summary>
    public async Task RecomecarAsync(Guid usuarioId, NovoCadastro dados, CancellationToken cancelamento = default)
    {
        var senhaHash = senhas.Hash(dados.Senha);
        var usuario = await db.Usuarios.SingleAsync(u => u.Id == usuarioId, cancelamento);
        usuario.ReiniciarCadastro(dados.Nome, dados.Email, dados.Celular, senhaHash, dados.VersaoTermoBiometria, relogio.GetUtcNow());
        try
        {
            await db.SaveChangesAsync(cancelamento);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: ViolacaoDeUnicidade })
        {
            throw new ConflitoDeCadastroException();
        }
    }
}
