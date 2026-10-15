using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Ministerio.Contratos;

namespace Ministerio.Tests.Integracao;

/// <summary>Apoio dos testes do painel: pedir o desafio e mandar a mudança de acesso, com o rosto (falso) do admin.</summary>
internal static class AcessoDeTeste
{
    public static async Task<Guid> DesafioDeAcessoAsync(this HttpClient cliente, Titular admin, Guid alvo, int? nivel, bool? ehAdmin)
    {
        var resposta = await cliente.Enviar(
            HttpMethod.Post,
            "/v1/biometria/desafios",
            admin.Sessao,
            new { finalidade = "ALTERAR_ACESSO", parametros = new { usuario_id = alvo, nivel, admin = ehAdmin } });
        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        return (await resposta.Ler<DesafioDeAcaoResponse>()).DesafioId;
    }

    /// <summary>Manda a mudança em multipart. Sem <paramref name="desafio"/>, abre um desafio para exatamente estes valores.</summary>
    public static async Task<HttpResponseMessage> AlterarAcessoAsync(
        this HttpClient cliente,
        Titular admin,
        Guid alvo,
        int? nivel,
        bool? ehAdmin = null,
        Guid? desafio = null,
        bool rosto = true,
        string? senha = null,
        bool semDesafio = false)
    {
        if (!semDesafio)
        {
            desafio ??= await cliente.DesafioDeAcessoAsync(admin, alvo, nivel, ehAdmin);
        }

        var requisicao = new HttpRequestMessage(HttpMethod.Put, $"/v1/admin/usuarios/{alvo}/acesso");
        requisicao.Headers.Authorization = new AuthenticationHeaderValue("Bearer", admin.Sessao.AccessToken);
        var corpo = new MultipartFormDataContent
        {
            { new StringContent(JsonSerializer.Serialize(new { nivel, admin = ehAdmin }, ClienteDeTeste.Json)), "dados" },
        };
        if (desafio is not null)
        {
            corpo.Add(new StringContent(desafio.Value.ToString()), "desafio_id");
        }

        if (senha is not null)
        {
            corpo.Add(new StringContent(senha), "senha");
        }

        if (rosto)
        {
            var arquivo = new ByteArrayContent(ClienteDeTeste.Foto);
            arquivo.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            corpo.Add(arquivo, "image", "rosto.jpg");
        }

        requisicao.Content = corpo;
        return await cliente.SendAsync(requisicao);
    }

    /// <summary>Promove (ou rebaixa) e confere que deu certo. É assim que os testes fabricam diretores e ministros.</summary>
    public static async Task DefinirNivelAsync(this HttpClient cliente, Titular admin, Titular alvo, int nivel)
    {
        var resposta = await cliente.AlterarAcessoAsync(admin, alvo.Sessao.Usuario.Id, nivel);
        Assert.Equal(HttpStatusCode.NoContent, resposta.StatusCode);
    }
}
