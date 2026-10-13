using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Banco.Contratos;
using Banco.Dominio;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Banco.Tests.Integracao;

/// <summary>Entrada por rosto: desafio de 90 s com uma tentativa de verdade, e o serviço facial só é chamado quando há o que comparar.</summary>
public class LoginFacialTests : IClassFixture<FaceEnrollTests.SemAtalho>, IDisposable
{
    private readonly ApiFactory _base;
    private readonly RelogioAjustavel _relogio = new();
    private readonly WebApplicationFactory<Program> _fabrica;
    private readonly HttpClient _cliente;

    public LoginFacialTests(FaceEnrollTests.SemAtalho semAtalho)
    {
        _base = semAtalho.Fabrica;
        _base.Facial.Falha = null;
        _base.Facial.Corresponde = true;
        _fabrica = _base.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddSingleton<TimeProvider>(_relogio)));
        _cliente = _fabrica.CreateClient();
    }

    public void Dispose()
    {
        _fabrica.Dispose();
        GC.SuppressFinalize(this);
    }

    private ClienteFacialFalso Facial => _base.Facial;

    private static async Task<string?> Code(HttpResponseMessage resposta)
    {
        using var doc = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private async Task<DesafioResponse> DesafioAsync(string cpf)
    {
        var resposta = await _cliente.PostAsJsonAsync("/v1/auth/login/desafio", new { cpf }, ClienteDeTeste.Json);
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        return await resposta.Ler<DesafioResponse>();
    }

    private Task<HttpResponseMessage> Rosto(Guid desafioId, byte[]? imagem = null) =>
        _cliente.EnviarFormularioAsync("/v1/auth/login/face", null, imagem ?? ClienteDeTeste.Foto, "image", ("desafio_id", desafioId.ToString()));

    private Task<HttpResponseMessage> LoginComSenha(string cpf, string senha) =>
        _cliente.PostAsJsonAsync("/v1/auth/login/senha", new { cpf, senha }, ClienteDeTeste.Json);

    [Fact]
    public async Task Rosto_que_confere_abre_a_sessao_e_o_servico_recebe_o_template_decifrado_no_limiar_de_login()
    {
        var (cpf, _, _) = await _cliente.CadastrarComRostoAsync();
        var desafio = await DesafioAsync(cpf);

        var resposta = await Rosto(desafio.DesafioId);
        var sessao = await resposta.Ler<SessaoResponse>();

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal("FACE", sessao.Metodo);
        Assert.Equal(ClienteFacialFalso.Vetor, Facial.UltimaReferencia);
        Assert.Equal(0.363m, Facial.UltimoLimiar);
        Assert.Equal(HttpStatusCode.OK, (await _cliente.Enviar(HttpMethod.Get, "/v1/contas/me", sessao)).StatusCode);
        Assert.DoesNotContain("score", await resposta.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Desafio_vale_uma_vez_so()
    {
        var (cpf, _, _) = await _cliente.CadastrarComRostoAsync();
        var desafio = await DesafioAsync(cpf);
        Assert.Equal(HttpStatusCode.OK, (await Rosto(desafio.DesafioId)).StatusCode);

        var segunda = await Rosto(desafio.DesafioId);

        Assert.Equal(HttpStatusCode.Unauthorized, segunda.StatusCode);
        Assert.Equal("FACE_NOT_VERIFIED", await Code(segunda));
    }

    [Fact]
    public async Task Rosto_recusado_gasta_o_desafio_e_conta_para_o_bloqueio_da_conta()
    {
        var (cpf, _, _) = await _cliente.CadastrarComRostoAsync();
        Facial.Corresponde = false;
        var desafio = await DesafioAsync(cpf);

        var recusado = await Rosto(desafio.DesafioId);
        Facial.Corresponde = true;
        var mesmoDesafio = await Rosto(desafio.DesafioId);

        Assert.Equal("FACE_NOT_VERIFIED", await Code(recusado));
        Assert.Equal(HttpStatusCode.Unauthorized, mesmoDesafio.StatusCode);

        // Mais quatro rostos errados completam cinco falhas: a senha certa passa a receber o bloqueio.
        Facial.Corresponde = false;
        for (var i = 0; i < 4; i++)
        {
            await Rosto((await DesafioAsync(cpf)).DesafioId);
        }

        var senhaCerta = await LoginComSenha(cpf, ClienteDeTeste.Senha);
        Assert.Equal(HttpStatusCode.Forbidden, senhaCerta.StatusCode);
        Assert.Equal("ACCOUNT_LOCKED", await Code(senhaCerta));

        // Rosto certo de conta bloqueada também só descobre o bloqueio depois de bater.
        Facial.Corresponde = true;
        var rostoCerto = await Rosto((await DesafioAsync(cpf)).DesafioId);
        Assert.Equal(HttpStatusCode.Forbidden, rostoCerto.StatusCode);
    }

    [Theory]
    [InlineData("rosto", "NO_FACE_DETECTED")]
    [InlineData("varios", "MULTIPLE_FACES")]
    public async Task Foto_sem_rosto_ou_com_varios_devolve_o_desafio_e_a_proxima_foto_entra(string tipo, string code)
    {
        var (cpf, _, _) = await _cliente.CadastrarComRostoAsync();
        var desafio = await DesafioAsync(cpf);
        Facial.Falha = tipo == "rosto" ? new RostoNaoDetectadoException() : new VariosRostosException();

        var recusada = await Rosto(desafio.DesafioId);
        Facial.Falha = null;
        var aceita = await Rosto(desafio.DesafioId);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, recusada.StatusCode);
        Assert.Equal(code, await Code(recusada));
        Assert.Equal(HttpStatusCode.OK, aceita.StatusCode);
    }

    [Fact]
    public async Task Depois_de_5_devolucoes_o_desafio_se_perde()
    {
        var (cpf, _, _) = await _cliente.CadastrarComRostoAsync();
        var desafio = await DesafioAsync(cpf);
        Facial.Falha = new RostoNaoDetectadoException();

        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Rosto(desafio.DesafioId)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Rosto(desafio.DesafioId)).StatusCode); // a 6ª gasta o desafio
        Facial.Falha = null;
        Assert.Equal(HttpStatusCode.Unauthorized, (await Rosto(desafio.DesafioId)).StatusCode);
    }

    [Fact]
    public async Task Servico_facial_fora_do_ar_devolve_503_sem_gastar_o_desafio()
    {
        var (cpf, _, _) = await _cliente.CadastrarComRostoAsync();
        var desafio = await DesafioAsync(cpf);
        Facial.Falha = new ServicoFacialIndisponivelException();

        var fora = await Rosto(desafio.DesafioId);
        Facial.Falha = null;
        var volta = await Rosto(desafio.DesafioId);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, fora.StatusCode);
        Assert.Equal("FACE_SERVICE_UNAVAILABLE", await Code(fora));
        Assert.Equal(HttpStatusCode.OK, volta.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginComSenha(cpf, ClienteDeTeste.Senha)).StatusCode); // a senha nunca dependeu do rosto
    }

    [Fact]
    public async Task Desafio_vencido_nao_vale()
    {
        var (cpf, _, _) = await _cliente.CadastrarComRostoAsync();
        var desafio = await DesafioAsync(cpf);

        _relogio.Avancar(TimeSpan.FromSeconds(91));
        var tarde = await Rosto(desafio.DesafioId);

        Assert.Equal(HttpStatusCode.Unauthorized, tarde.StatusCode);
        Assert.Equal("FACE_NOT_VERIFIED", await Code(tarde));
    }

    [Fact]
    public async Task Cpf_inexistente_conta_pendente_e_conta_sem_rosto_respondem_igual_e_o_servico_facial_nem_e_chamado()
    {
        var (pendenteCpf, _) = await _cliente.CadastrarPendenteAsync();
        var chamadasAntes = Facial.Verificacoes;

        var inexistente = await DesafioAsync("52998224725");
        var pendente = await DesafioAsync(pendenteCpf);
        var respostas = new[]
        {
            await Rosto(inexistente.DesafioId),
            await Rosto(pendente.DesafioId),
            await Rosto(Guid.NewGuid()), // desafio que nunca existiu
        };

        Assert.All(respostas, r => Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode));
        foreach (var r in respostas)
        {
            Assert.Equal("FACE_NOT_VERIFIED", await Code(r));
        }

        Assert.Equal(chamadasAntes, Facial.Verificacoes);
        Assert.Equal(inexistente.Instrucao, pendente.Instrucao);
    }

    [Fact]
    public async Task Pedido_mal_formado_e_recusado_antes_de_tudo()
    {
        var semCpf = await _cliente.PostAsJsonAsync("/v1/auth/login/desafio", new { cpf = "529.982.247-25" }, ClienteDeTeste.Json);
        var semDesafio = await _cliente.EnviarFormularioAsync("/v1/auth/login/face", null, ClienteDeTeste.Foto, "image", ("desafio_id", "nao-e-guid"));
        var semImagem = await _cliente.EnviarFormularioAsync("/v1/auth/login/face", null, null, "image", ("desafio_id", Guid.NewGuid().ToString()));
        var grande = await Rosto(Guid.NewGuid(), new byte[(2 * 1024 * 1024) + 1]);

        Assert.Equal(HttpStatusCode.BadRequest, semCpf.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, semDesafio.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, semImagem.StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, grande.StatusCode);
    }

    [Fact]
    public async Task Cinco_tentativas_por_minuto_por_ip_e_o_resto_toma_429()
    {
        using var limitada = new ApiFactory { FatorDeLimite = 1 };
        var cliente = limitada.CreateClient();

        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await cliente.PostAsJsonAsync("/v1/auth/login/desafio", new { cpf = "52998224725" }, ClienteDeTeste.Json)).StatusCode);
        }

        var barrada = await cliente.PostAsJsonAsync("/v1/auth/login/desafio", new { cpf = "52998224725" }, ClienteDeTeste.Json);
        Assert.Equal(HttpStatusCode.TooManyRequests, barrada.StatusCode);
    }
}
