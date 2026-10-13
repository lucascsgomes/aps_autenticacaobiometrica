using System.Net;
using Banco.Dominio;
using Banco.Dominio.Biometria;
using Banco.Dominio.Usuarios;
using Banco.Infra.Facial;
using Banco.Infra.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Banco.Infra.Autenticacao;

public sealed record DesafioDeLogin(Guid Id, DateTimeOffset ExpiraEm);

/// <summary>Entrada por rosto: abre um desafio para o CPF e depois confere a foto contra o template cifrado do usuário.</summary>
public sealed class ServicoDeLoginFacial(
    BancoDbContext banco,
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
        var usuarioId = await banco.Usuarios
            .Where(u => u.Cpf == cpf && u.Status == StatusUsuario.Ativo)
            .Select(u => (Guid?)u.Id)
            .SingleOrDefaultAsync(cancelamento);
        var desafio = await desafios.AbrirDeLoginAsync(usuarioId, limiares.Login, ip, cancelamento);
        return new DesafioDeLogin(desafio.Id, desafio.ExpiraEm);
    }

    public async Task<SessaoEmitida> EntrarAsync(Guid desafioId, byte[] imagem, string? idDaRequisicao, CancellationToken cancelamento = default)
    {
        var consumo = await desafios.ConsumirAsync(desafioId, FinalidadeDoDesafio.Login, MetodoDoDesafio.Face, cancelamento)
            ?? throw new FaceNaoVerificadaException();

        // Sem dono, conta que não está ativa ou sem rosto cadastrado: mesma resposta de rosto errado, e o serviço facial nem é chamado.
        if (consumo.UsuarioId is not { } usuarioId
            || await banco.Usuarios.SingleOrDefaultAsync(u => u.Id == usuarioId && u.Status == StatusUsuario.Ativo, cancelamento) is not { } usuario
            || await banco.Templates.AsNoTracking().SingleOrDefaultAsync(t => t.UsuarioId == usuarioId, cancelamento) is not { } template)
        {
            throw new FaceNaoVerificadaException();
        }

        var referencia = cifra.Decifrar(usuarioId, template.EmbeddingCifrado, template.Nonce);
        var agora = relogio.GetUtcNow();

        ResultadoDaVerificacao resultado;
        try
        {
            resultado = await facial.VerificarAsync(imagem, referencia, consumo.Limiar, idDaRequisicao, cancelamento);
        }
        catch (Exception e) when (e is RostoNaoDetectadoException or VariosRostosException or ImagemInvalidaException or ServicoFacialIndisponivelException)
        {
            // A foto não pôde ser avaliada: o desafio volta. Só o serviço fora do ar não conta como uma das 5 devoluções.
            await desafios.DevolverAsync(desafioId, consumo, contar: e is not ServicoFacialIndisponivelException, CancellationToken.None);
            throw;
        }

        if (!resultado.Corresponde)
        {
            // Durante o bloqueio uma falha não estica o tempo: ele já está valendo.
            if (!usuario.EstaBloqueado(agora))
            {
                usuario.RegistrarFalhaDeCredencial(agora);
                await banco.SaveChangesAsync(cancelamento);
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
        await banco.SaveChangesAsync(cancelamento);

        var contaId = await banco.Contas.Where(c => c.UsuarioId == usuarioId).Select(c => c.Id).SingleAsync(cancelamento);
        return new SessaoEmitida(usuarioId, usuario.Nome, contaId, token);
    }
}

/// <summary>Limiares do cosseno por finalidade. Login 0,363 (padrão do SFace); confirmação de transferência será mais rígida.</summary>
public sealed record LimiaresFaciais(decimal Login);
