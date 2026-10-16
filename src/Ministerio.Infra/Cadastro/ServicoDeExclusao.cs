using Ministerio.Infra.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Ministerio.Infra.Cadastro;

/// <summary>
/// Apaga o cadastro de verdade: o usuário, o vetor do rosto (cascata), os desafios (cascata) e as sessões. O histórico de acessos fica,
/// sem o vínculo com o usuário, porque o relatório precisa continuar dizendo que houve a tentativa.
/// </summary>
public sealed class ServicoDeExclusao(MinisterioDbContext db)
{
    public async Task ExcluirAsync(Guid usuarioId, CancellationToken cancelamento = default)
    {
        await using var transacao = await db.Database.BeginTransactionAsync(cancelamento);
        var sessoes = db.RefreshTokens.Where(t => t.UsuarioId == usuarioId);
        // Uma sessão aponta para a que a substituiu: solta os apontamentos antes de apagar.
        await sessoes.ExecuteUpdateAsync(s => s.SetProperty(t => t.SubstituidoPor, (Guid?)null), cancelamento);
        await sessoes.ExecuteDeleteAsync(cancelamento);
        await db.Usuarios.Where(u => u.Id == usuarioId).ExecuteDeleteAsync(cancelamento);
        await transacao.CommitAsync(cancelamento);
    }
}
