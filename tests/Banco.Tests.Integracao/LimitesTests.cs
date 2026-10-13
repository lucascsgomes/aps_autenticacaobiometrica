using System.Net;
using System.Text.Json;
using Banco.Contratos;
using Banco.Dominio;
using Banco.Dominio.Contas;
using Banco.Dominio.Transferencias;
using Banco.Infra.Persistencia;
using Banco.Infra.Transferencias;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Banco.Tests.Integracao;

/// <summary>Limite diurno e noturno: aplicado na intenção (cortesia) e dentro da transação do dinheiro (a que vale).</summary>
public class LimitesTests : IClassFixture<ApiFactory>, IDisposable
{
    private readonly RelogioAjustavel _relogio = new();
    private readonly WebApplicationFactory<Program> _fabrica;
    private readonly HttpClient _cliente;

    public LimitesTests(ApiFactory fabrica)
    {
        _fabrica = fabrica.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddSingleton<TimeProvider>(_relogio)));
        _cliente = _fabrica.CreateClient();
    }

    public void Dispose()
    {
        _fabrica.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<(Titular Origem, Titular Destino)> DoisTitularesAsync() =>
        (await _cliente.NovoTitularAsync(), await _cliente.NovoTitularAsync("Beatriz Souza Lima"));

    private static async Task<string?> Code(HttpResponseMessage resposta)
    {
        using var doc = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private Task<HttpResponseMessage> Intencao(Titular de, Titular para, long centavos) =>
        _cliente.Enviar(HttpMethod.Post, "/v1/transferencias/intencoes", de.Sessao, new { chave_destino = para.Cpf, valor_centavos = centavos });

    /// <summary>Intenção e confirmação por senha. Devolve a resposta do confirmar, ou a da intenção se ela já foi recusada.</summary>
    private async Task<HttpResponseMessage> TransferirAsync(Titular de, Titular para, long centavos)
    {
        var intencao = await Intencao(de, para, centavos);
        if (intencao.StatusCode != HttpStatusCode.Created)
        {
            return intencao;
        }

        var id = (await intencao.Ler<IntencaoResponse>()).TransferenciaId;
        return await _cliente.Enviar(HttpMethod.Post, $"/v1/transferencias/{id}/confirmar", de.Sessao, new { senha = ClienteDeTeste.Senha }, Guid.NewGuid().ToString());
    }

    private Task<HttpResponseMessage> AlterarLimites(Titular quem, object corpo) =>
        _cliente.Enviar(HttpMethod.Put, "/v1/contas/me/limites", quem.Sessao, corpo);

    [Fact]
    public async Task De_noite_o_limite_e_de_R1000_e_de_dia_volta_ao_diurno()
    {
        var (ana, bia) = await DoisTitularesAsync();

        _relogio.Fixar(RelogioAjustavel.HojeAs(23));
        Assert.Equal(HttpStatusCode.OK, (await TransferirAsync(ana, bia, 60_000)).StatusCode);
        var estouraNoturno = await Intencao(ana, bia, 50_000);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, estouraNoturno.StatusCode);
        Assert.Equal("NIGHT_LIMIT_EXCEEDED", await Code(estouraNoturno));

        _relogio.Fixar(RelogioAjustavel.HojeAs(12));
        Assert.Equal(HttpStatusCode.OK, (await TransferirAsync(ana, bia, 300_000)).StatusCode); // R$ 3.600 no dia
        await AlterarLimites(ana, new { limite_diurno_centavos = 400_000 }); // o saldo inicial é igual ao limite padrão, então o diurno só estoura se ele baixar
        var estouraDiurno = await Intencao(ana, bia, 50_000);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, estouraDiurno.StatusCode);
        Assert.Equal("DAILY_LIMIT_EXCEEDED", await Code(estouraDiurno));
    }

    [Fact]
    public async Task Limite_reduzido_entre_a_intencao_e_a_confirmacao_falha_dentro_da_transacao_sem_mover_dinheiro()
    {
        var (ana, bia) = await DoisTitularesAsync();
        _relogio.Fixar(RelogioAjustavel.HojeAs(12));
        var intencao = await (await Intencao(ana, bia, 100_000)).Ler<IntencaoResponse>();

        Assert.Equal(HttpStatusCode.OK, (await AlterarLimites(ana, new { limite_diurno_centavos = 50_000, limite_noturno_centavos = 50_000 })).StatusCode);
        var confirmar = await _cliente.Enviar(HttpMethod.Post, $"/v1/transferencias/{intencao.TransferenciaId}/confirmar", ana.Sessao, new { senha = ClienteDeTeste.Senha }, Guid.NewGuid().ToString());
        var situacao = await (await _cliente.Enviar(HttpMethod.Get, $"/v1/transferencias/{intencao.TransferenciaId}", ana.Sessao)).Ler<TransferenciaResponse>();
        var conta = await (await _cliente.Enviar(HttpMethod.Get, "/v1/contas/me", ana.Sessao)).Ler<ContaMeResponse>();

        Assert.Equal(HttpStatusCode.UnprocessableEntity, confirmar.StatusCode);
        Assert.Equal("DAILY_LIMIT_EXCEEDED", await Code(confirmar));
        Assert.Equal("FALHA", situacao.Status);
        Assert.Equal("LIMITE_DIURNO", situacao.MotivoFalha);
        Assert.Equal(500_000, conta.SaldoCentavos);
    }

    [Fact]
    public async Task Transferencias_simultaneas_da_mesma_conta_nao_passam_juntas_pelo_limite()
    {
        var (ana, bia) = await DoisTitularesAsync();
        _relogio.Fixar(RelogioAjustavel.HojeAs(12));
        Assert.Equal(HttpStatusCode.OK, (await AlterarLimites(ana, new { limite_diurno_centavos = 100_000 })).StatusCode);

        // Cinco transferências de R$ 300 já confirmadas, executadas ao mesmo tempo contra um limite de R$ 1.000.
        var ids = new List<Guid>();
        await using (var escopo = _fabrica.Services.CreateAsyncScope())
        {
            var banco = escopo.ServiceProvider.GetRequiredService<BancoDbContext>();
            for (var i = 0; i < 5; i++)
            {
                var t = Transferencia.Criar(ana.Conta.Id, bia.Conta.Id, Dinheiro.DeCentavos(30_000), null, DateTimeOffset.UtcNow);
                t.Confirmar(MetodoConfirmacao.Senha);
                banco.Transferencias.Add(t);
                ids.Add(t.Id);
            }

            await banco.SaveChangesAsync();
        }

        var status = await Task.WhenAll(ids.Select(async id =>
        {
            await using var escopo = _fabrica.Services.CreateAsyncScope();
            return await escopo.ServiceProvider.GetRequiredService<ExecutorDeTransferencia>().ExecutarAsync(id);
        }));

        Assert.Equal(3, status.Count(s => s == StatusTransferencia.Concluida));
        Assert.Equal(2, status.Count(s => s == StatusTransferencia.Falha));
        var conta = await (await _cliente.Enviar(HttpMethod.Get, "/v1/contas/me", ana.Sessao)).Ler<ContaMeResponse>();
        Assert.Equal(500_000 - 90_000, conta.SaldoCentavos);
    }

    [Fact]
    public async Task Consulta_mostra_limites_uso_e_a_janela_atual()
    {
        var (ana, bia) = await DoisTitularesAsync();
        _relogio.Fixar(RelogioAjustavel.HojeAs(21));
        await TransferirAsync(ana, bia, 25_000);

        var noite = await (await _cliente.Enviar(HttpMethod.Get, "/v1/contas/me/limites", ana.Sessao)).Ler<LimitesResponse>();
        _relogio.Fixar(RelogioAjustavel.HojeAs(12));
        var dia = await (await _cliente.Enviar(HttpMethod.Get, "/v1/contas/me/limites", ana.Sessao)).Ler<LimitesResponse>();

        Assert.Equal(new LimitesResponse(500_000, 100_000, "20:00", "06:00", 25_000, "NOTURNA"), noite);
        Assert.Equal(new LimitesResponse(500_000, 100_000, "20:00", "06:00", 25_000, "DIURNA"), dia);
    }

    [Fact]
    public async Task Reduzir_vale_na_hora_e_aumentar_pede_confirmacao()
    {
        var ana = await _cliente.NovoTitularAsync();

        var reduz = await AlterarLimites(ana, new { limite_diurno_centavos = 200_000 });
        var aumenta = await AlterarLimites(ana, new { limite_diurno_centavos = 200_001 });
        var encurtaANoite = await AlterarLimites(ana, new { inicio_noturno = "22:00" });

        Assert.Equal(HttpStatusCode.OK, reduz.StatusCode);
        Assert.Equal(200_000, (await reduz.Ler<LimitesResponse>()).LimiteDiurnoCentavos);
        Assert.Equal(HttpStatusCode.Forbidden, aumenta.StatusCode);
        Assert.Equal("STEP_UP_REQUIRED", await Code(aumenta));
        Assert.Contains("ALTERAR_LIMITE", await aumenta.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Forbidden, encurtaANoite.StatusCode);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"limite_diurno_centavos\": -1}")]
    [InlineData("{\"inicio_noturno\": \"21:00\"}")]
    [InlineData("{\"limite_diurno_centavos\": 50000}")] // fica abaixo do noturno de 100.000
    public async Task Alteracao_invalida_devolve_400(string corpo)
    {
        var ana = await _cliente.NovoTitularAsync();

        var resposta = await AlterarLimites(ana, JsonSerializer.Deserialize<JsonElement>(corpo));

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    [Fact]
    public async Task Limites_exigem_login()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _cliente.Enviar(HttpMethod.Get, "/v1/contas/me/limites", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _cliente.Enviar(HttpMethod.Put, "/v1/contas/me/limites", null, new { })).StatusCode);
    }
}
