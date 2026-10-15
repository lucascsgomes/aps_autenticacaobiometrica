using Microsoft.EntityFrameworkCore;
using Ministerio.Infra.Acesso;
using Ministerio.Infra.Persistencia;

namespace Ministerio.Api.Seguranca;

/// <summary>O que a pessoa pode fazer agora: o nível e o perfil de admin vêm do banco (e da lista de admins da configuração).</summary>
internal readonly record struct Permissao(int Nivel, bool Admin);

internal static class PermissaoDoUsuario
{
    /// <summary>Lê a cada pedido, não do token: o admin muda o nível de alguém e vale na hora, sem esperar a sessão vencer.</summary>
    public static async Task<Permissao> ObterAsync(this MinisterioDbContext db, ServicoDeAdmins admins, Guid usuarioId, CancellationToken cancelamento)
    {
        var u = await db.Usuarios.AsNoTracking().Where(x => x.Id == usuarioId).Select(x => new { x.Cpf, x.Nivel, x.Admin }).SingleAsync(cancelamento);
        return new Permissao(u.Nivel, admins.EhAdmin(u.Cpf, u.Admin));
    }
}
