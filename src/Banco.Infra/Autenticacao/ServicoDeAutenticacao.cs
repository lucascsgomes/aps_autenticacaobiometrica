using Banco.Dominio;
using Banco.Dominio.Usuarios;
using Banco.Infra.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Banco.Infra.Autenticacao;

/// <summary>Quem acabou de entrar. O access token (JWT) é emitido pela API a partir disto.</summary>
public sealed record SessaoEmitida(Guid UsuarioId, string Nome, Guid ContaId, string RefreshToken);

public sealed class ServicoDeAutenticacao(
    BancoDbContext banco,
    ServicoDeSenha senhas,
    RefreshTokenServico refresh,
    TimeProvider relogio)
{
    public async Task<SessaoEmitida> LoginComSenhaAsync(string cpf, string senha, CancellationToken cancelamento = default)
    {
        var agora = relogio.GetUtcNow();
        var usuario = await banco.Usuarios.SingleOrDefaultAsync(u => u.Cpf == cpf, cancelamento);
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
                await banco.SaveChangesAsync(cancelamento);
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
        await banco.SaveChangesAsync(cancelamento);
        return await MontarAsync(usuario.Id, token, cancelamento);
    }

    public async Task<SessaoEmitida> RenovarAsync(string refreshToken, CancellationToken cancelamento = default)
    {
        var (novo, usuarioId) = await refresh.RotacionarAsync(refreshToken, cancelamento);
        return await MontarAsync(usuarioId, novo, cancelamento);
    }

    public Task SairAsync(string refreshToken, CancellationToken cancelamento = default) =>
        refresh.RevogarAsync(refreshToken, cancelamento);

    private async Task<SessaoEmitida> MontarAsync(Guid usuarioId, string refreshToken, CancellationToken cancelamento)
    {
        var dados = await banco.Usuarios
            .Where(u => u.Id == usuarioId)
            .Join(banco.Contas, u => u.Id, c => c.UsuarioId, (u, c) => new { u.Nome, ContaId = c.Id })
            .SingleAsync(cancelamento);
        return new SessaoEmitida(usuarioId, dados.Nome, dados.ContaId, refreshToken);
    }
}
