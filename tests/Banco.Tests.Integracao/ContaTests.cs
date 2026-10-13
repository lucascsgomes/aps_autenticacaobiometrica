using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Banco.Contratos;
using Banco.Dominio;
using Banco.Dominio.Contas;
using Banco.Dominio.Transferencias;
using Banco.Infra.Persistencia;
using Banco.Infra.Transferencias;
using Microsoft.Extensions.DependencyInjection;

namespace Banco.Tests.Integracao;

/// <summary>Extrato por cursor, chaves de transferência e rate limit, com a API inteira e PostgreSQL real.</summary>
public class ContaTests(ApiFactory fabrica) : IClassFixture<ApiFactory>
{
    private const string Senha = "uma-senha-longa-e-unica";
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    private static int _sequencia = 1000;

    private readonly HttpClient _cliente = fabrica.CreateClient();

    private sealed record Titular(string Cpf, SessaoResponse Sessao, ContaMeResponse Conta);

    private async Task<Titular> NovoTitularAsync(string nome = "Ana Beatriz Moreira")
    {
        var n = Interlocked.Increment(ref _sequencia);
        var cpf = AuthTests.CpfValido(n);
        var resposta = await _cliente.PostAsJsonAsync("/v1/auth/register", new
        {
            nome,
            cpf,
            email = $"conta{n}@exemplo.com",
            senha = Senha,
            consentimento_biometrico = new { aceito = true, versao_termo = "2026-10-v1" },
        }, Json);
        var sessao = (await Ler<RegistrarResponse>(resposta)).Sessao!;
        var conta = await Ler<ContaMeResponse>(await Pegar("/v1/contas/me", sessao));
        return new Titular(cpf, sessao, conta);
    }

    private static async Task<T> Ler<T>(HttpResponseMessage resposta) => (await resposta.Content.ReadFromJsonAsync<T>(Json))!;

    private Task<HttpResponseMessage> Enviar(HttpMethod metodo, string rota, SessaoResponse? sessao)
    {
        var requisicao = new HttpRequestMessage(metodo, rota);
        if (sessao is not null)
        {
            requisicao.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessao.AccessToken);
        }

        return _cliente.SendAsync(requisicao);
    }

    private Task<HttpResponseMessage> Pegar(string rota, SessaoResponse? sessao) => Enviar(HttpMethod.Get, rota, sessao);

    /// <summary>Transferência concluída de verdade, pelo mesmo executor que a API usará depois da confirmação.</summary>
    private async Task TransferirAsync(Titular origem, Titular destino, long centavos, string? descricao = null)
    {
        await using var escopo = fabrica.Services.CreateAsyncScope();
        var banco = escopo.ServiceProvider.GetRequiredService<BancoDbContext>();
        var t = Transferencia.Criar(origem.Conta.Id, destino.Conta.Id, Dinheiro.DeCentavos(centavos), descricao, DateTimeOffset.UtcNow);
        t.Confirmar(MetodoConfirmacao.Senha);
        banco.Transferencias.Add(t);
        await banco.SaveChangesAsync();
        var status = await escopo.ServiceProvider.GetRequiredService<ExecutorDeTransferencia>().ExecutarAsync(t.Id);
        Assert.Equal(StatusTransferencia.Concluida, status);
    }

    [Fact]
    public async Task Extrato_pagina_por_cursor_sem_repetir_nem_pular_e_com_saldo_apos_cada_linha()
    {
        var ana = await NovoTitularAsync();
        var bia = await NovoTitularAsync("Beatriz Souza Lima");
        for (var i = 1; i <= 4; i++)
        {
            await TransferirAsync(ana, bia, i * 1_000, $"Parcela {i}");
        }

        var vistos = new List<ExtratoItemDto>();
        string? cursor = null;
        var paginas = 0;
        do
        {
            var rota = $"/v1/contas/me/extrato?limit=2{(cursor is null ? "" : "&cursor=" + cursor)}";
            var resposta = await Pegar(rota, ana.Sessao);
            Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
            var pagina = await Ler<ExtratoResponse>(resposta);
            vistos.AddRange(pagina.Data);
            cursor = pagina.NextCursor;
            paginas++;
            Assert.Equal(cursor is not null, pagina.HasMore);
        }
        while (cursor is not null);

        Assert.Equal(3, paginas); // 5 lançamentos: crédito inicial + 4 débitos
        Assert.Equal(5, vistos.Count);
        Assert.Equal(5, vistos.Select(v => v.LancamentoId).Distinct().Count());
        Assert.Equal(vistos.OrderByDescending(v => v.LancamentoId).Select(v => v.LancamentoId), vistos.Select(v => v.LancamentoId));
        Assert.Equal(ana.Conta.SaldoCentavos - 10_000, vistos[0].SaldoAposCentavos);
        Assert.Equal("D", vistos[0].Tipo);
        Assert.Equal("Parcela 4", vistos[0].Descricao);
        Assert.Equal(4_000, vistos[0].ValorCentavos);
        Assert.Equal("Beatriz L***", vistos[0].Contraparte.Nome);
        Assert.Equal("Banco APS", vistos[^1].Contraparte.Nome);
        Assert.Equal(ana.Conta.SaldoCentavos, vistos[^1].SaldoAposCentavos);
    }

    [Fact]
    public async Task Extrato_filtra_por_tipo_e_por_data_e_cada_usuario_ve_so_o_seu()
    {
        var ana = await NovoTitularAsync();
        var bia = await NovoTitularAsync("Beatriz Souza Lima");
        await TransferirAsync(ana, bia, 2_500);

        var saidas = await Ler<ExtratoResponse>(await Pegar("/v1/contas/me/extrato?tipo=D", ana.Sessao));
        var entradasDaBia = await Ler<ExtratoResponse>(await Pegar("/v1/contas/me/extrato?tipo=C", bia.Sessao));
        var hoje = DateTime.UtcNow.AddHours(-3).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var deHoje = await Ler<ExtratoResponse>(await Pegar($"/v1/contas/me/extrato?de={hoje}&ate={hoje}", ana.Sessao));
        var deOntem = await Ler<ExtratoResponse>(await Pegar("/v1/contas/me/extrato?ate=2020-01-01", ana.Sessao));

        Assert.Single(saidas.Data);
        Assert.Equal(2, entradasDaBia.Data.Count);
        Assert.All(entradasDaBia.Data, e => Assert.Equal("C", e.Tipo));
        Assert.Equal(2, deHoje.Data.Count);
        Assert.Empty(deOntem.Data);
        Assert.DoesNotContain(saidas.Data, s => entradasDaBia.Data.Any(e => e.LancamentoId == s.LancamentoId));
    }

    [Theory]
    [InlineData("limit=0", "limit")]
    [InlineData("limit=101", "limit")]
    [InlineData("cursor=nao-e-cursor", "cursor")]
    [InlineData("cursor=MXwx&tipo=X", "tipo")]
    [InlineData("de=10/10/2026", "de")]
    public async Task Extrato_recusa_parametro_invalido_com_400(string consulta, string campo)
    {
        var ana = await NovoTitularAsync();

        var resposta = await Pegar($"/v1/contas/me/extrato?{consulta}", ana.Sessao);

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Contains(campo, await resposta.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Extrato_e_chaves_exigem_login()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Pegar("/v1/contas/me/extrato", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Pegar("/v1/chaves/52998224725", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Enviar(HttpMethod.Delete, $"/v1/contas/me/chaves/{Guid.NewGuid()}", null)).StatusCode);
    }

    [Fact]
    public async Task Cadastro_cria_a_chave_CPF_e_a_conta_mostra_so_a_versao_mascarada()
    {
        var ana = await NovoTitularAsync();

        var chave = Assert.Single(ana.Conta.Chaves);
        Assert.Equal("CPF", chave.Tipo);
        Assert.StartsWith("***.", chave.ValorMascarado, StringComparison.Ordinal);
        Assert.DoesNotContain(ana.Cpf, JsonSerializer.Serialize(ana.Conta, Json), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Consulta_de_chave_devolve_nome_mascarado_e_diz_se_e_propria()
    {
        var ana = await NovoTitularAsync();
        var bia = await NovoTitularAsync("Beatriz Souza Lima");

        var deTerceiro = await Ler<ConsultaChaveResponse>(await Pegar($"/v1/chaves/{bia.Cpf}", ana.Sessao));
        var propria = await Ler<ConsultaChaveResponse>(await Pegar($"/v1/chaves/{ana.Cpf}", ana.Sessao));

        Assert.Equal("Beatriz L***", deTerceiro.NomeMascarado);
        Assert.False(deTerceiro.Propria);
        Assert.DoesNotContain(bia.Cpf, JsonSerializer.Serialize(deTerceiro, Json), StringComparison.Ordinal);
        Assert.True(propria.Propria);
    }

    [Theory]
    [InlineData("529.982.247-25", HttpStatusCode.UnprocessableEntity, "INVALID_KEY_FORMAT")]
    [InlineData("52998224724", HttpStatusCode.UnprocessableEntity, "INVALID_KEY_FORMAT")]
    [InlineData("Maria@Exemplo.com", HttpStatusCode.UnprocessableEntity, "INVALID_KEY_FORMAT")]
    [InlineData("52998224725", HttpStatusCode.NotFound, "KEY_NOT_FOUND")]
    [InlineData("%2B5511900000000", HttpStatusCode.NotFound, "KEY_NOT_FOUND")]
    public async Task Consulta_de_chave_com_formato_errado_ou_sem_dono_devolve_o_erro_certo(string valor, HttpStatusCode status, string code)
    {
        var ana = await NovoTitularAsync();

        var resposta = await Pegar($"/v1/chaves/{valor}", ana.Sessao);

        Assert.Equal(status, resposta.StatusCode);
        Assert.Contains(code, await resposta.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task So_o_dono_apaga_a_chave_e_depois_ela_nao_existe_mais()
    {
        var ana = await NovoTitularAsync();
        var bia = await NovoTitularAsync("Beatriz Souza Lima");
        var chaveDaAna = ana.Conta.Chaves.Single().Id;

        var alheia = await Enviar(HttpMethod.Delete, $"/v1/contas/me/chaves/{chaveDaAna}", bia.Sessao);
        var propria = await Enviar(HttpMethod.Delete, $"/v1/contas/me/chaves/{chaveDaAna}", ana.Sessao);
        var consulta = await Pegar($"/v1/chaves/{ana.Cpf}", bia.Sessao);

        Assert.Equal(HttpStatusCode.NotFound, alheia.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, propria.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, consulta.StatusCode);
    }

    [Fact]
    public async Task Passar_do_limite_de_login_devolve_429_com_Retry_After()
    {
        using var limitada = new ApiFactory { FatorDeLimite = 1 };
        var cliente = limitada.CreateClient();
        var corpo = new { cpf = "52998224725", senha = "qualquer-senha-errada" };

        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await cliente.PostAsJsonAsync("/v1/auth/login/senha", corpo, Json)).StatusCode);
        }

        var barrada = await cliente.PostAsJsonAsync("/v1/auth/login/senha", corpo, Json);

        Assert.Equal(HttpStatusCode.TooManyRequests, barrada.StatusCode);
        Assert.True(barrada.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        Assert.Contains("RATE_LIMITED", await barrada.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }
}
