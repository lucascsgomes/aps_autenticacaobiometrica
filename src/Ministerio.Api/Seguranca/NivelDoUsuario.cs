using Ministerio.Infra.Acesso;
using Ministerio.Infra.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Ministerio.Api.Seguranca;

internal static class NivelDoUsuario
{
    /// <summary>O nível sai do CPF do usuário e da lista configurada, a cada pedido: mudar a lista vale na hora, sem refazer cadastro.</summary>
    public static async Task<int> ObterAsync(this MinisterioDbContext db, ServicoDeNiveis niveis, Guid usuarioId, CancellationToken cancelamento) =>
        niveis.De(await db.Usuarios.AsNoTracking().Where(u => u.Id == usuarioId).Select(u => u.Cpf).SingleAsync(cancelamento));
}
