using Banco.Dominio;
using Banco.Dominio.Biometria;
using Banco.Infra.Facial;
using Banco.Infra.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Banco.Infra.Autenticacao;

/// <summary>Troca da senha e ciclo de vida do rosto cadastrado (consultar, trocar, excluir). Quem chama já confirmou a credencial.</summary>
public sealed class ServicoDeCredenciais(BancoDbContext banco, ServicoDeSenha senhas, CifraDeTemplate cifra, RefreshTokenServico refresh, TimeProvider relogio)
{
    public Task<TemplateFacial?> RostoAsync(Guid usuarioId, CancellationToken cancelamento = default) =>
        banco.Templates.AsNoTracking().SingleOrDefaultAsync(t => t.UsuarioId == usuarioId, cancelamento);

    /// <summary>Troca o rosto (ou cadastra de novo, depois de uma exclusão) e encerra as sessões abertas, na mesma transação.</summary>
    public async Task<TemplateFacial> SubstituirRostoAsync(Guid usuarioId, byte[] embedding, CancellationToken cancelamento = default)
    {
        var (cifrado, nonce) = cifra.Cifrar(usuarioId, embedding);
        var novo = TemplateFacial.Criar(usuarioId, cifrado, nonce, cifra.Versao, relogio.GetUtcNow());

        await using var transacao = await banco.Database.BeginTransactionAsync(cancelamento);
        await banco.Templates.Where(t => t.UsuarioId == usuarioId).ExecuteDeleteAsync(cancelamento);
        banco.Templates.Add(novo);
        await banco.SaveChangesAsync(cancelamento);
        await refresh.RevogarTodasAsync(usuarioId, cancelamento);
        await transacao.CommitAsync(cancelamento);
        return novo;
    }

    /// <summary>Apaga o template. A conta segue com a senha; login e transferência por rosto voltam quando houver novo cadastro.</summary>
    public async Task ExcluirRostoAsync(Guid usuarioId, CancellationToken cancelamento = default)
    {
        if (await banco.Templates.Where(t => t.UsuarioId == usuarioId).ExecuteDeleteAsync(cancelamento) == 0)
        {
            throw new BiometriaNaoCadastradaException();
        }
    }

    public async Task TrocarSenhaAsync(Guid usuarioId, string novaSenha, CancellationToken cancelamento = default)
    {
        var usuario = await banco.Usuarios.SingleAsync(u => u.Id == usuarioId, cancelamento);
        usuario.TrocarSenha(senhas.Hash(novaSenha), relogio.GetUtcNow());

        await using var transacao = await banco.Database.BeginTransactionAsync(cancelamento);
        await banco.SaveChangesAsync(cancelamento);
        await refresh.RevogarTodasAsync(usuarioId, cancelamento);
        await transacao.CommitAsync(cancelamento);
    }
}
