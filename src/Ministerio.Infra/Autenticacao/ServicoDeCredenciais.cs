using Ministerio.Dominio;
using Ministerio.Dominio.Biometria;
using Ministerio.Infra.Facial;
using Ministerio.Infra.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Ministerio.Infra.Autenticacao;

/// <summary>Troca da senha e ciclo de vida do rosto cadastrado (consultar, trocar, excluir). Quem chama já confirmou a credencial.</summary>
public sealed class ServicoDeCredenciais(MinisterioDbContext db, ServicoDeSenha senhas, CifraDeTemplate cifra, RefreshTokenServico refresh, TimeProvider relogio)
{
    public Task<TemplateFacial?> RostoAsync(Guid usuarioId, CancellationToken cancelamento = default) =>
        db.Templates.AsNoTracking().SingleOrDefaultAsync(t => t.UsuarioId == usuarioId, cancelamento);

    /// <summary>Troca o rosto (ou cadastra de novo, depois de uma exclusão) e encerra as sessões abertas, na mesma transação.</summary>
    public async Task<TemplateFacial> SubstituirRostoAsync(Guid usuarioId, byte[] embedding, CancellationToken cancelamento = default)
    {
        var (cifrado, nonce) = cifra.Cifrar(usuarioId, embedding);
        var novo = TemplateFacial.Criar(usuarioId, cifrado, nonce, cifra.Versao, relogio.GetUtcNow());

        await using var transacao = await db.Database.BeginTransactionAsync(cancelamento);
        await db.Templates.Where(t => t.UsuarioId == usuarioId).ExecuteDeleteAsync(cancelamento);
        db.Templates.Add(novo);
        await db.SaveChangesAsync(cancelamento);
        await refresh.RevogarTodasAsync(usuarioId, cancelamento);
        await transacao.CommitAsync(cancelamento);
        return novo;
    }

    /// <summary>Apaga o template. A conta segue com a senha; login e transferência por rosto voltam quando houver novo cadastro.</summary>
    public async Task ExcluirRostoAsync(Guid usuarioId, CancellationToken cancelamento = default)
    {
        if (await db.Templates.Where(t => t.UsuarioId == usuarioId).ExecuteDeleteAsync(cancelamento) == 0)
        {
            throw new BiometriaNaoCadastradaException();
        }
    }

    public async Task TrocarSenhaAsync(Guid usuarioId, string novaSenha, CancellationToken cancelamento = default)
    {
        var usuario = await db.Usuarios.SingleAsync(u => u.Id == usuarioId, cancelamento);
        usuario.TrocarSenha(senhas.Hash(novaSenha), relogio.GetUtcNow());

        await using var transacao = await db.Database.BeginTransactionAsync(cancelamento);
        await db.SaveChangesAsync(cancelamento);
        await refresh.RevogarTodasAsync(usuarioId, cancelamento);
        await transacao.CommitAsync(cancelamento);
    }
}
