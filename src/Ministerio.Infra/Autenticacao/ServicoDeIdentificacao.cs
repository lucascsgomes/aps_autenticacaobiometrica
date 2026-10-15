using Ministerio.Dominio;
using Ministerio.Dominio.Usuarios;
using Ministerio.Infra.Facial;
using Ministerio.Infra.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Ministerio.Infra.Autenticacao;

/// <summary>
/// Entrada só pelo rosto, sem informar CPF: o serviço facial compara a foto com todos os rostos cadastrados (1 para N) e diz quem é.
/// Recusa quando ninguém passa do limiar ou quando duas pessoas ficam parecidas demais.
/// </summary>
public sealed class ServicoDeIdentificacao(
    MinisterioDbContext db,
    IClienteFacial facial,
    CifraDeTemplate cifra,
    RefreshTokenServico refresh,
    LimiaresFaciais limiares,
    TimeProvider relogio)
{
    // shortcut: o face-service aceita até 1000 candidatos por chamada; com mais cadastros seria preciso dividir em lotes.
    public const int MaximoDeCandidatos = 1000;

    public async Task<SessaoEmitida> EntrarAsync(byte[] imagem, string? idDaRequisicao, CancellationToken cancelamento = default)
    {
        var modelos = await db.Templates.AsNoTracking()
            .Where(t => db.Usuarios.Any(u => u.Id == t.UsuarioId && u.Status == StatusUsuario.Ativo))
            .OrderBy(t => t.CriadoEm)
            .Take(MaximoDeCandidatos)
            .ToListAsync(cancelamento);

        // Ninguém cadastrado: não há o que comparar, e o serviço facial nem é chamado.
        if (modelos.Count == 0)
        {
            throw new FaceNaoVerificadaException();
        }

        var candidatos = modelos
            .Select(t => new Candidato(t.UsuarioId.ToString(), cifra.Decifrar(t.UsuarioId, t.EmbeddingCifrado, t.Nonce)))
            .ToList();
        var resultado = await facial.IdentificarAsync(imagem, candidatos, limiares.Login, limiares.Margem, idDaRequisicao, cancelamento);
        if (!resultado.Identificado || !Guid.TryParse(resultado.CandidatoId, out var usuarioId))
        {
            throw new FaceNaoVerificadaException();
        }

        var usuario = await db.Usuarios.SingleAsync(u => u.Id == usuarioId, cancelamento);
        var agora = relogio.GetUtcNow();

        // Só um rosto reconhecido descobre que o acesso está bloqueado.
        if (usuario.EstaBloqueado(agora))
        {
            throw new ContaBloqueadaException(usuario.BloqueadoAte!.Value - agora);
        }

        usuario.RegistrarCredencialCorreta(agora);
        var token = refresh.NovaFamilia(usuarioId);
        await db.SaveChangesAsync(cancelamento);
        return new SessaoEmitida(usuarioId, usuario.Nome, usuario.Cpf, usuario.Nivel, usuario.Admin, token);
    }
}
