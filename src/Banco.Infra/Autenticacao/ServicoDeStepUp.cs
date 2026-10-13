using System.Net;
using Banco.Dominio;
using Banco.Dominio.Biometria;
using Banco.Dominio.Usuarios;
using Banco.Infra.Facial;
using Banco.Infra.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Banco.Infra.Autenticacao;

/// <summary>
/// Confirmação de ações sensíveis (aumentar limite, criar chave) por rosto ou senha. O desafio nasce amarrado aos parâmetros da ação
/// e uma tentativa só: quem confirma recalcula o hash a partir do que veio no pedido, então outro valor não passa.
/// </summary>
public sealed class ServicoDeStepUp(
    BancoDbContext banco,
    ServicoDeDesafios desafios,
    IClienteFacial facial,
    CifraDeTemplate cifra,
    ServicoDeSenha senhas,
    LimiaresFaciais limiares,
    TimeProvider relogio)
{
    public Task<DesafioBiometrico> AbrirAsync(Guid usuarioId, FinalidadeDoDesafio finalidade, string parametrosCanonicos, IPAddress? ip, CancellationToken cancelamento = default) =>
        desafios.AbrirDeAcaoAsync(usuarioId, finalidade, parametrosCanonicos, limiares.Transferencia, ip, cancelamento);

    /// <summary>
    /// Gasta o desafio e confere a credencial: com <paramref name="imagem"/> é o rosto (no limiar de ação sensível), senão a senha.
    /// Credencial errada gasta o desafio e conta para o bloqueio; foto sem rosto, com vários, ilegível ou serviço fora do ar devolvem o desafio.
    /// Sai sem exceção só quando a ação pode seguir.
    /// </summary>
    public async Task ConfirmarAsync(
        Guid usuarioId,
        FinalidadeDoDesafio finalidade,
        string parametrosCanonicos,
        Guid desafioId,
        string? senha,
        byte[]? imagem,
        string? idDaRequisicao,
        CancellationToken cancelamento = default)
    {
        var comRosto = imagem is not null;
        var metodo = comRosto ? MetodoDoDesafio.Face : MetodoDoDesafio.Senha;
        var nonce = await desafios.NonceAsync(desafioId, usuarioId, cancelamento) ?? throw Falha(comRosto);
        var amarra = new Amarra(usuarioId, null, DesafioBiometrico.HashDaAcao(usuarioId, finalidade, parametrosCanonicos, nonce));
        var consumo = await desafios.ConsumirAsync(desafioId, finalidade, metodo, amarra, cancelamento) ?? throw Falha(comRosto);

        var usuario = await banco.Usuarios.SingleAsync(u => u.Id == usuarioId && u.Status == StatusUsuario.Ativo, cancelamento);
        bool confere;
        if (comRosto)
        {
            var template = await banco.Templates.AsNoTracking().SingleOrDefaultAsync(t => t.UsuarioId == usuarioId, cancelamento) ?? throw Falha(true);
            var referencia = cifra.Decifrar(usuarioId, template.EmbeddingCifrado, template.Nonce);
            confere = (await desafios.VerificarAsync(facial, desafioId, consumo, imagem!, referencia, idDaRequisicao, cancelamento)).Corresponde;
        }
        else
        {
            confere = senhas.Verificar(usuario.SenhaHash, senha ?? "");
        }

        await ConcluirAsync(usuario, confere, comRosto, cancelamento);
    }

    /// <summary>Confirmação só por senha, sem desafio: para a exclusão do rosto. Conta para o bloqueio como as demais.</summary>
    public async Task ConfirmarSenhaAsync(Guid usuarioId, string senha, CancellationToken cancelamento = default)
    {
        var usuario = await banco.Usuarios.SingleAsync(u => u.Id == usuarioId && u.Status == StatusUsuario.Ativo, cancelamento);
        await ConcluirAsync(usuario, senhas.Verificar(usuario.SenhaHash, senha), false, cancelamento);
    }

    private async Task ConcluirAsync(Usuario usuario, bool confere, bool comRosto, CancellationToken cancelamento)
    {
        var agora = relogio.GetUtcNow();
        if (!confere)
        {
            // Durante o bloqueio uma falha não estica o tempo: ele já está valendo.
            if (!usuario.EstaBloqueado(agora))
            {
                usuario.RegistrarFalhaDeCredencial(agora);
                await banco.SaveChangesAsync(cancelamento);
            }

            throw Falha(comRosto);
        }

        // Só uma credencial correta descobre que a conta está bloqueada.
        if (usuario.EstaBloqueado(agora))
        {
            throw new ContaBloqueadaException(usuario.BloqueadoAte!.Value - agora);
        }

        usuario.RegistrarCredencialCorreta(agora);
        await banco.SaveChangesAsync(cancelamento);
    }

    private static NegocioException Falha(bool comRosto) => comRosto ? new FaceNaoVerificadaException() : new SenhaNaoVerificadaException();
}
