using Ministerio.Dominio;
using Ministerio.Dominio.Usuarios;
using Ministerio.Infra.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Ministerio.Infra.Autenticacao;

/// <summary>Quem acabou de entrar. O access token (JWT) é emitido pela API a partir disto.</summary>
public sealed record SessaoEmitida(Guid UsuarioId, string Nome, string Cpf, int Nivel, bool Admin, string RefreshToken);

public sealed class ServicoDeAutenticacao(
    MinisterioDbContext db,
    ServicoDeSenha senhas,
    RefreshTokenServico refresh,
    TimeProvider relogio)
{
    public async Task<SessaoEmitida> LoginComSenhaAsync(string cpf, string senha, CancellationToken cancelamento = default)
    {
        var agora = relogio.GetUtcNow();
        var usuario = await db.Usuarios.SingleOrDefaultAsync(u => u.Cpf == cpf, cancelamento);
        var senhaCorreta = senhas.Verificar(usuario?.SenhaHash, senha);

        // CPF inexistente, cadastro pendente e senha errada respondem igual.
        if (usuario is null || usuario.Status != StatusUsuario.Ativo)
        {
            throw new CredenciaisInvalidasException();
        }

        if (!senhaCorreta)
        {
            // Durante o bloqueio uma senha errada não estica o tempo: o bloqueio já está valendo.
            if (!usuario.EstaBloqueado(agora))
            {
                usuario.RegistrarFalhaDeCredencial(agora);
                await db.SaveChangesAsync(cancelamento);
            }

            throw new CredenciaisInvalidasException();
        }

        // Só uma credencial correta descobre que a conta está bloqueada.
        if (usuario.EstaBloqueado(agora))
        {
            throw new ContaBloqueadaException(usuario.BloqueadoAte!.Value - agora);
        }

        usuario.RegistrarCredencialCorreta(agora);
        var token = refresh.NovaFamilia(usuario.Id);
        await db.SaveChangesAsync(cancelamento);
        return new SessaoEmitida(usuario.Id, usuario.Nome, usuario.Cpf, usuario.Nivel, usuario.Admin, token);
    }

    public async Task<SessaoEmitida> RenovarAsync(string refreshToken, CancellationToken cancelamento = default)
    {
        var (novo, usuarioId) = await refresh.RotacionarAsync(refreshToken, cancelamento);
        var dados = await db.Usuarios.Where(u => u.Id == usuarioId).Select(u => new { u.Nome, u.Cpf, u.Nivel, u.Admin }).SingleAsync(cancelamento);
        return new SessaoEmitida(usuarioId, dados.Nome, dados.Cpf, dados.Nivel, dados.Admin, novo);
    }

    public Task SairAsync(string refreshToken, CancellationToken cancelamento = default) =>
        refresh.RevogarAsync(refreshToken, cancelamento);
}
