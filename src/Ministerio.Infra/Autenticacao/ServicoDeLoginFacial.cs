using System.Net;
using Ministerio.Dominio;
using Ministerio.Dominio.Biometria;
using Ministerio.Dominio.Usuarios;
using Ministerio.Infra.Facial;
using Ministerio.Infra.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Ministerio.Infra.Autenticacao;

public sealed record DesafioDeLogin(Guid Id, DateTimeOffset ExpiraEm);

/// <summary>Entrada por rosto: abre um desafio para o CPF e depois confere a foto contra o template cifrado do usuário.</summary>
public sealed class ServicoDeLoginFacial(
    MinisterioDbContext db,
    ServicoDeDesafios desafios,
    IClienteFacial facial,
    CifraDeTemplate cifra,
    RefreshTokenServico refresh,
    LimiaresFaciais limiares,
    TimeProvider relogio)
{
    /// <summary>
    /// Responde igual para CPF inexistente, conta sem rosto e conta pendente: quem consulta não descobre quem tem conta.
    /// O desafio de um CPF que não existe nasce sem dono e termina em FACE_NOT_VERIFIED.
    /// </summary>
    public async Task<DesafioDeLogin> AbrirDesafioAsync(string cpf, IPAddress? ip, CancellationToken cancelamento = default)
    {
        var usuarioId = await db.Usuarios
            .Where(u => u.Cpf == cpf && u.Status == StatusUsuario.Ativo)
            .Select(u => (Guid?)u.Id)
            .SingleOrDefaultAsync(cancelamento);
        var desafio = await desafios.AbrirDeLoginAsync(usuarioId, limiares.Login, ip, cancelamento);
        return new DesafioDeLogin(desafio.Id, desafio.ExpiraEm);
    }

    public async Task<SessaoEmitida> EntrarAsync(Guid desafioId, byte[] imagem, string? idDaRequisicao, CancellationToken cancelamento = default)
    {
        var consumo = await desafios.ConsumirAsync(desafioId, FinalidadeDoDesafio.Login, MetodoDoDesafio.Face, cancelamento: cancelamento)
            ?? throw new FaceNaoVerificadaException();

        // Sem dono, conta que não está ativa ou sem rosto cadastrado: mesma resposta de rosto errado, e o serviço facial nem é chamado.
        if (consumo.UsuarioId is not { } usuarioId
            || await db.Usuarios.SingleOrDefaultAsync(u => u.Id == usuarioId && u.Status == StatusUsuario.Ativo, cancelamento) is not { } usuario
            || await db.Templates.AsNoTracking().SingleOrDefaultAsync(t => t.UsuarioId == usuarioId, cancelamento) is not { } template)
        {
            throw new FaceNaoVerificadaException();
        }

        var referencia = cifra.Decifrar(usuarioId, template.EmbeddingCifrado, template.Nonce);
        var agora = relogio.GetUtcNow();

        var resultado = await desafios.VerificarAsync(facial, desafioId, consumo, imagem, referencia, idDaRequisicao, cancelamento);

        if (!resultado.Corresponde)
        {
            // Durante o bloqueio uma falha não estica o tempo: ele já está valendo.
            if (!usuario.EstaBloqueado(agora))
            {
                usuario.RegistrarFalhaDeCredencial(agora);
                await db.SaveChangesAsync(cancelamento);
            }

            throw new FaceNaoVerificadaException();
        }

        // Só uma credencial correta descobre que a conta está bloqueada.
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

/// <summary>
/// Limiares do cosseno por finalidade. Login 0,363 (padrão do SFace); níveis restritos mais rígidos (0,400 e 0,500), porque o que
/// se protege vale mais; margem mínima entre o primeiro e o segundo candidato na identificação 1 para N.
/// </summary>
public sealed record LimiaresFaciais(decimal Login, decimal Nivel2, decimal Nivel3, decimal Margem)
{
    /// <summary>Nível 2 usa o seu; o nível 3 e as trocas de rosto e de senha (que dão acesso a tudo) usam o mais rígido.</summary>
    public decimal Do(Ministerio.Dominio.Biometria.FinalidadeDoDesafio finalidade) =>
        finalidade == Ministerio.Dominio.Biometria.FinalidadeDoDesafio.AcessoNivel2 ? Nivel2 : Nivel3;
}
