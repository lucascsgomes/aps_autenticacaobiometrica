using Banco.Dominio;

namespace Banco.Api.Endpoints;

/// <summary>Leitura da foto enviada em multipart, no campo <c>image</c>, com o teto de 2 MB que o face-service também impõe.</summary>
internal static class Imagens
{
    public const int TamanhoMaximoDaImagem = 2 * 1024 * 1024;
    public const int TamanhoMaximoDaRequisicao = TamanhoMaximoDaImagem + 64 * 1024;

    public static async Task<IFormCollection> LerFormularioAsync(HttpRequest requisicao, CancellationToken cancelamento) =>
        requisicao.HasFormContentType ? await requisicao.ReadFormAsync(cancelamento) : throw new ImagemInvalidaException();

    public static byte[] LerImagem(IFormCollection formulario, string campo = "image")
    {
        if (formulario.Files.GetFile(campo) is not { Length: > 0 } arquivo)
        {
            throw new ImagemInvalidaException();
        }

        if (arquivo.Length > TamanhoMaximoDaImagem)
        {
            throw new ImagemGrandeException();
        }

        using var fluxo = arquivo.OpenReadStream();
        using var memoria = new MemoryStream((int)arquivo.Length);
        fluxo.CopyTo(memoria);
        return memoria.ToArray();
    }
}
