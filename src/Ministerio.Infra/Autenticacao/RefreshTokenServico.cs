using System.Security.Cryptography;
using Ministerio.Dominio;
using Ministerio.Dominio.Usuarios;
using Ministerio.Infra.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Ministerio.Infra.Autenticacao;

/// <summary>Emite, rotaciona e revoga refresh tokens. Reuso de um token já usado revoga a família inteira.</summary>
public sealed class RefreshTokenServico(MinisterioDbContext db, TimeProvider relogio)
{
    public static readonly TimeSpan Validade = TimeSpan.FromDays(7);

    /// <summary>Abre uma família nova (um login). Só adiciona ao contexto: quem chama salva, na mesma transação do login.</summary>
    public string NovaFamilia(Guid usuarioId) => Emitir(usuarioId, Guid.NewGuid(), relogio.GetUtcNow()).Token;

    /// <summary>Troca o token por um novo da mesma família. Devolve o novo token e o dono.</summary>
    public async Task<(string Token, Guid UsuarioId)> RotacionarAsync(string tokenRecebido, CancellationToken cancelamento = default)
    {
        var hash = Hash(tokenRecebido);
        await using var transacao = await db.Database.BeginTransactionAsync(cancelamento);

        // O lock serializa dois usos simultâneos do mesmo token: o segundo já o encontra usado.
        var atual = await db.RefreshTokens
            .FromSql($"SELECT * FROM refresh_tokens WHERE token_hash = {hash} FOR UPDATE")
            .SingleOrDefaultAsync(cancelamento);
        var agora = relogio.GetUtcNow();

        if (atual is null || atual.Expirou(agora))
        {
            throw new NaoAutenticadoException();
        }

        if (atual.JaFoiUsadoOuRevogado)
        {
            await RevogarFamiliaAsync(atual.FamiliaId, agora, cancelamento);
            await transacao.CommitAsync(cancelamento);
            throw new NaoAutenticadoException();
        }

        var ativo = await db.Usuarios.AnyAsync(u => u.Id == atual.UsuarioId && u.Status == StatusUsuario.Ativo, cancelamento);
        if (!ativo)
        {
            throw new NaoAutenticadoException();
        }

        var (token, novo) = Emitir(atual.UsuarioId, atual.FamiliaId, agora);
        atual.MarcarComoUsado(novo.Id, agora);
        await db.SaveChangesAsync(cancelamento);
        await transacao.CommitAsync(cancelamento);
        return (token, atual.UsuarioId);
    }

    /// <summary>Logout. Idempotente: token desconhecido não é erro.</summary>
    public async Task RevogarAsync(string tokenRecebido, CancellationToken cancelamento = default)
    {
        var hash = Hash(tokenRecebido);
        var familia = await db.RefreshTokens.Where(t => t.TokenHash == hash).Select(t => (Guid?)t.FamiliaId).SingleOrDefaultAsync(cancelamento);
        if (familia is { } id)
        {
            await RevogarFamiliaAsync(id, relogio.GetUtcNow(), cancelamento);
        }
    }

    /// <summary>Encerra todas as sessões do usuário (troca de senha ou de rosto). Os access tokens já emitidos valem até vencer.</summary>
    public Task<int> RevogarTodasAsync(Guid usuarioId, CancellationToken cancelamento = default) =>
        db.RefreshTokens
            .Where(t => t.UsuarioId == usuarioId && t.RevogadoEm == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevogadoEm, relogio.GetUtcNow()), cancelamento);

    private Task<int> RevogarFamiliaAsync(Guid familiaId, DateTimeOffset agora, CancellationToken cancelamento) =>
        db.RefreshTokens
            .Where(t => t.FamiliaId == familiaId && t.RevogadoEm == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevogadoEm, agora), cancelamento);

    private (string Token, RefreshToken Entidade) Emitir(Guid usuarioId, Guid familiaId, DateTimeOffset agora)
    {
        var token = System.Buffers.Text.Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        var entidade = RefreshToken.Emitir(usuarioId, familiaId, Hash(token), agora, Validade);
        db.RefreshTokens.Add(entidade);
        return (token, entidade);
    }

    private static byte[] Hash(string token) => SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token));
}
