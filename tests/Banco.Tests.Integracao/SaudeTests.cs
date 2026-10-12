using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Banco.Tests.Integracao;

public class SaudeTests(ApiFactory fabrica) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _cliente = fabrica.CreateClient();

    [Theory]
    [InlineData("/health")]
    [InlineData("/ready")]
    public async Task Endpoints_de_saude_respondem_200(string rota)
    {
        var resposta = await _cliente.GetAsync(new Uri(rota, UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal("Healthy", await resposta.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Ready_responde_503_sem_vazar_detalhe_quando_o_banco_nao_responde()
    {
        await using var semBanco = new ApiFactory { ConexaoForcada = "Host=localhost;Port=1;Database=x;Username=x;Password=segredo-do-banco;Timeout=2" };

        var resposta = await semBanco.CreateClient().GetAsync(new Uri("/ready", UriKind.Relative));
        var corpo = await resposta.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, resposta.StatusCode);
        Assert.DoesNotContain("segredo-do-banco", corpo, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Excecao_nao_tratada_vira_problem_details_sem_vazar_detalhe()
    {
        var resposta = await _cliente.GetAsync(new Uri(ApiFactory.RotaQueFalha, UriKind.Relative));
        var corpo = await resposta.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, resposta.StatusCode);
        Assert.Equal("application/problem+json", resposta.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain("segredo interno", corpo, StringComparison.Ordinal);
        using var json = JsonDocument.Parse(corpo);
        Assert.Equal(500, json.RootElement.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task Rota_inexistente_responde_404_em_problem_details()
    {
        var resposta = await _cliente.GetAsync(new Uri("/nao-existe", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
        Assert.Equal("application/problem+json", resposta.Content.Headers.ContentType?.MediaType);
    }
}
