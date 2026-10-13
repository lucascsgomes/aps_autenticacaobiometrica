using System.Net;

namespace Banco.Tests.Integracao;

/// <summary>Só a interface web configurada em <c>Cors:Origens</c> pode chamar a API do navegador.</summary>
public class CorsTests(ApiFactory fabrica) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _cliente = fabrica.CreateClient();

    private Task<HttpResponseMessage> Preflight(string origem)
    {
        var pedido = new HttpRequestMessage(HttpMethod.Options, "/v1/auth/login/senha");
        pedido.Headers.Add("Origin", origem);
        pedido.Headers.Add("Access-Control-Request-Method", "POST");
        pedido.Headers.Add("Access-Control-Request-Headers", "content-type,authorization");
        return _cliente.SendAsync(pedido);
    }

    [Fact]
    public async Task Origem_da_interface_web_e_liberada()
    {
        var resposta = await Preflight("http://localhost:5090");

        Assert.Equal(HttpStatusCode.NoContent, resposta.StatusCode);
        Assert.Equal("http://localhost:5090", resposta.Headers.GetValues("Access-Control-Allow-Origin").Single());
    }

    [Fact]
    public async Task Outra_origem_nao_recebe_permissao()
    {
        var resposta = await Preflight("https://site-qualquer.example");

        Assert.False(resposta.Headers.Contains("Access-Control-Allow-Origin"));
    }
}
