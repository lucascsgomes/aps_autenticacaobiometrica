using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Banco.Contratos;
using Banco.Dominio;

namespace Banco.Tests.Integracao;

/// <summary>Aumentar limite e criar chave exigem rosto ou senha, amarrados ao pedido: o desafio vale para aquela mudança e mais nada.</summary>
public class StepUpTests : IClassFixture<FaceEnrollTests.SemAtalho>
{
    private readonly ApiFactory _fabrica;
    private readonly HttpClient _cliente;

    public StepUpTests(FaceEnrollTests.SemAtalho semAtalho)
    {
        _fabrica = semAtalho.Fabrica;
        _fabrica.Facial.Falha = null;
        _fabrica.Facial.Corresponde = true;
        _cliente = _fabrica.CreateClient();
    }

    private ClienteFacialFalso Facial => _fabrica.Facial;

    private static async Task<string?> Code(HttpResponseMessage resposta)
    {
        using var doc = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private async Task<Guid> DesafioAsync(Titular quem, string finalidade, object parametros)
    {
        var resposta = await _cliente.Enviar(HttpMethod.Post, "/v1/biometria/desafios", quem.Sessao, new { finalidade, parametros });
        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        var corpo = await resposta.Ler<DesafioDeAcaoResponse>();
        Assert.Equal(finalidade, corpo.Finalidade);
        return corpo.DesafioId;
    }

    private Task<HttpResponseMessage> Multipart(HttpMethod metodo, string rota, Titular quem, object dados, Guid desafioId, string? senha, bool rosto)
    {
        var requisicao = new HttpRequestMessage(metodo, rota);
        requisicao.Headers.Authorization = new AuthenticationHeaderValue("Bearer", quem.Sessao.AccessToken);
        var corpo = new MultipartFormDataContent
        {
            { new StringContent(JsonSerializer.Serialize(dados, ClienteDeTeste.Json)), "dados" },
            { new StringContent(desafioId.ToString()), "desafio_id" },
        };
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
        return _cliente.SendAsync(requisicao);
    }

    private Task<HttpResponseMessage> PutLimites(Titular quem, object dados, Guid desafioId, string? senha = ClienteDeTeste.Senha, bool rosto = false) =>
        Multipart(HttpMethod.Put, "/v1/contas/me/limites", quem, dados, desafioId, senha, rosto);

    private Task<HttpResponseMessage> PostChave(Titular quem, object dados, Guid desafioId, string? senha = ClienteDeTeste.Senha, bool rosto = false) =>
        Multipart(HttpMethod.Post, "/v1/contas/me/chaves", quem, dados, desafioId, senha, rosto);

    private async Task<long> DiurnoAsync(Titular quem) =>
        (await (await _cliente.Enviar(HttpMethod.Get, "/v1/contas/me/limites", quem.Sessao)).Ler<LimitesResponse>()).LimiteDiurnoCentavos;

    /// <summary>Titular com rosto e limite diurno já reduzido a 400 mil, para haver o que aumentar.</summary>
    private async Task<Titular> ComLimiteReduzidoAsync()
    {
        var ana = await _cliente.NovoTitularComRostoAsync();
        Assert.Equal(HttpStatusCode.OK, (await _cliente.Enviar(HttpMethod.Put, "/v1/contas/me/limites", ana.Sessao, new { limite_diurno_centavos = 400_000 })).StatusCode);
        return ana;
    }

    [Fact]
    public async Task Aumentar_o_limite_com_a_senha_vale_so_para_aquele_pedido()
    {
        var ana = await ComLimiteReduzidoAsync();
        var pedido = new { limite_diurno_centavos = 450_000 };
        var desafio = await DesafioAsync(ana, "ALTERAR_LIMITE", pedido);

        var outroValor = await PutLimites(ana, new { limite_diurno_centavos = 500_000 }, desafio);
        Assert.Equal(HttpStatusCode.Unauthorized, outroValor.StatusCode);
        Assert.Equal("PASSWORD_NOT_VERIFIED", await Code(outroValor));
        Assert.Equal(400_000, await DiurnoAsync(ana));

        var desafioCerto = await DesafioAsync(ana, "ALTERAR_LIMITE", pedido);
        var resposta = await PutLimites(ana, pedido, desafioCerto);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal(450_000, (await resposta.Ler<LimitesResponse>()).LimiteDiurnoCentavos);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PutLimites(ana, pedido, desafioCerto)).StatusCode); // uma vez só
    }

    [Fact]
    public async Task Aumentar_o_limite_com_o_rosto_usa_o_limiar_de_acao_sensivel()
    {
        var ana = await ComLimiteReduzidoAsync();
        var pedido = new { limite_diurno_centavos = 500_000 };
        var desafio = await DesafioAsync(ana, "ALTERAR_LIMITE", pedido);

        var resposta = await PutLimites(ana, pedido, desafio, senha: null, rosto: true);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal(0.450m, Facial.UltimoLimiar);
        Assert.Equal(500_000, await DiurnoAsync(ana));
    }

    [Fact]
    public async Task Senha_errada_e_rosto_recusado_gastam_o_desafio_e_nao_mudam_nada()
    {
        var ana = await ComLimiteReduzidoAsync();
        var pedido = new { limite_diurno_centavos = 450_000 };
        var comSenhaErrada = await DesafioAsync(ana, "ALTERAR_LIMITE", pedido);
        var comRostoErrado = await DesafioAsync(ana, "ALTERAR_LIMITE", pedido);

        var senha = await PutLimites(ana, pedido, comSenhaErrada, senha: "senha-errada-123");
        Facial.Corresponde = false;
        var rosto = await PutLimites(ana, pedido, comRostoErrado, senha: null, rosto: true);
        Facial.Corresponde = true;

        Assert.Equal("PASSWORD_NOT_VERIFIED", await Code(senha));
        Assert.Equal("FACE_NOT_VERIFIED", await Code(rosto));
        Assert.Equal(HttpStatusCode.Unauthorized, (await PutLimites(ana, pedido, comSenhaErrada)).StatusCode);
        Assert.Equal(400_000, await DiurnoAsync(ana));
    }

    [Fact]
    public async Task Foto_sem_rosto_devolve_o_desafio_e_desafio_de_outro_usuario_nao_vale()
    {
        var ana = await ComLimiteReduzidoAsync();
        var bia = await ComLimiteReduzidoAsync();
        var pedido = new { limite_diurno_centavos = 450_000 };
        var desafio = await DesafioAsync(ana, "ALTERAR_LIMITE", pedido);

        Facial.Falha = new RostoNaoDetectadoException();
        var semRosto = await PutLimites(ana, pedido, desafio, senha: null, rosto: true);
        Facial.Falha = null;
        var deOutro = await PutLimites(bia, pedido, desafio);
        var aceita = await PutLimites(ana, pedido, desafio, senha: null, rosto: true);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, semRosto.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, deOutro.StatusCode);
        Assert.Equal(HttpStatusCode.OK, aceita.StatusCode);
    }

    [Fact]
    public async Task Cinco_senhas_erradas_em_acoes_bloqueiam_a_conta()
    {
        var ana = await ComLimiteReduzidoAsync();
        var pedido = new { limite_diurno_centavos = 450_000 };
        for (var i = 0; i < 5; i++)
        {
            await PutLimites(ana, pedido, await DesafioAsync(ana, "ALTERAR_LIMITE", pedido), senha: "senha-errada-123");
        }

        var certa = await PutLimites(ana, pedido, await DesafioAsync(ana, "ALTERAR_LIMITE", pedido));

        Assert.Equal(HttpStatusCode.Forbidden, certa.StatusCode);
        Assert.Equal("ACCOUNT_LOCKED", await Code(certa));
        Assert.Equal(400_000, await DiurnoAsync(ana));
    }

    [Fact]
    public async Task Criar_chave_exige_desafio_e_a_chave_aparece_na_conta()
    {
        var ana = await _cliente.NovoTitularComRostoAsync();
        var sem = await _cliente.Enviar(HttpMethod.Post, "/v1/contas/me/chaves", ana.Sessao, new { tipo = "EMAIL", valor = "ana.nova@exemplo.com" });
        Assert.Equal(HttpStatusCode.Forbidden, sem.StatusCode);
        Assert.Equal("STEP_UP_REQUIRED", await Code(sem));
        Assert.Contains("NOVA_CHAVE", await sem.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var email = new { tipo = "EMAIL", valor = "ana.nova@exemplo.com" };
        var criada = await PostChave(ana, email, await DesafioAsync(ana, "NOVA_CHAVE", email));
        var aleatoria = new { tipo = "ALEATORIA" };
        var gerada = await (await PostChave(ana, aleatoria, await DesafioAsync(ana, "NOVA_CHAVE", aleatoria), senha: null, rosto: true)).Ler<ChaveCriadaResponse>();

        Assert.Equal(HttpStatusCode.Created, criada.StatusCode);
        Assert.Equal("ALEATORIA", gerada.Tipo);
        Assert.Matches("^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$", gerada.Valor);
        var conta = await (await _cliente.Enviar(HttpMethod.Get, "/v1/contas/me", ana.Sessao)).Ler<ContaMeResponse>();
        Assert.Equal(["CPF", "EMAIL", "ALEATORIA"], conta.Chaves.Select(c => c.Tipo));
    }

    [Fact]
    public async Task Chave_em_uso_formato_ruim_e_limite_de_cinco()
    {
        var ana = await _cliente.NovoTitularComRostoAsync();
        var bia = await _cliente.NovoTitularComRostoAsync();
        var email = new { tipo = "EMAIL", valor = $"{Guid.NewGuid():N}@exemplo.com" };
        Assert.Equal(HttpStatusCode.Created, (await PostChave(bia, email, await DesafioAsync(bia, "NOVA_CHAVE", email))).StatusCode);

        var emUso = await PostChave(ana, email, await DesafioAsync(ana, "NOVA_CHAVE", email));
        var celularRuim = new { tipo = "CELULAR", valor = "11999990000" };
        var ruim = await PostChave(ana, celularRuim, await DesafioAsync(ana, "NOVA_CHAVE", celularRuim));

        Assert.Equal(HttpStatusCode.Conflict, emUso.StatusCode);
        Assert.Equal("KEY_ALREADY_EXISTS", await Code(emUso));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, ruim.StatusCode);
        Assert.Equal("INVALID_KEY_FORMAT", await Code(ruim));

        // A conta já tem a chave CPF: mais quatro completam cinco, e a sexta é recusada.
        var aleatoria = new { tipo = "ALEATORIA" };
        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(HttpStatusCode.Created, (await PostChave(ana, aleatoria, await DesafioAsync(ana, "NOVA_CHAVE", aleatoria))).StatusCode);
        }

        var sexta = await PostChave(ana, aleatoria, await DesafioAsync(ana, "NOVA_CHAVE", aleatoria));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, sexta.StatusCode);
        Assert.Equal("KEY_LIMIT_REACHED", await Code(sexta));
    }

    [Theory]
    [InlineData("""{"finalidade":"TROCAR_SENHA","parametros":{}}""", "finalidade")]
    [InlineData("""{"finalidade":"ALTERAR_LIMITE"}""", "corpo")]
    [InlineData("""{"finalidade":"ALTERAR_LIMITE","parametros":{"inicio_noturno":"21:00"}}""", "inicio_noturno")]
    [InlineData("""{"finalidade":"NOVA_CHAVE","parametros":{"tipo":"CPF","valor":"52998224725"}}""", "tipo")]
    [InlineData("""{"finalidade":"NOVA_CHAVE","parametros":{"tipo":"ALEATORIA","valor":"x"}}""", "valor")]
    public async Task Pedido_de_desafio_invalido_devolve_400_com_o_campo(string json, string campo)
    {
        var ana = await _cliente.NovoTitularComRostoAsync();
        var requisicao = new HttpRequestMessage(HttpMethod.Post, "/v1/biometria/desafios") { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
        requisicao.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ana.Sessao.AccessToken);

        var resposta = await _cliente.SendAsync(requisicao);

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Contains(campo, await resposta.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Senha_e_foto_juntas_ou_nenhuma_das_duas_sao_recusadas_sem_gastar_o_desafio()
    {
        var ana = await ComLimiteReduzidoAsync();
        var pedido = new { limite_diurno_centavos = 450_000 };
        var desafio = await DesafioAsync(ana, "ALTERAR_LIMITE", pedido);

        var ambas = await PutLimites(ana, pedido, desafio, senha: ClienteDeTeste.Senha, rosto: true);
        var nenhuma = await PutLimites(ana, pedido, desafio, senha: null);

        Assert.Equal(HttpStatusCode.BadRequest, ambas.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, nenhuma.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PutLimites(ana, pedido, desafio)).StatusCode);
    }
}
