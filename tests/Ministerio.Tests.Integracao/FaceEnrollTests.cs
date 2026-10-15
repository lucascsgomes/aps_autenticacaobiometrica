using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Ministerio.Contratos;
using Ministerio.Dominio;
using Ministerio.Infra.Facial;
using Ministerio.Infra.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ministerio.Tests.Integracao;

/// <summary>Cadastro de produção: o usuário nasce pendente e só vira cliente quando o rosto entra.</summary>
public class FaceEnrollTests : IClassFixture<FaceEnrollTests.SemAtalho>
{
    /// <summary>Sem o atalho de desenvolvimento: é o caminho que roda em produção.</summary>
    public sealed class SemAtalho : IDisposable
    {
        public ApiFactory Fabrica { get; } = new() { CadastroSemRosto = false };

        public void Dispose()
        {
            Fabrica.Dispose();
            GC.SuppressFinalize(this);
        }
    }

    private readonly ApiFactory _fabrica;
    private readonly HttpClient _cliente;

    public FaceEnrollTests(SemAtalho semAtalho)
    {
        _fabrica = semAtalho.Fabrica;
        _fabrica.Facial.Falha = null;
        _cliente = _fabrica.CreateClient();
    }

    private Task<(string Cpf, RegistrarResponse Cadastro)> PendenteAsync() => _cliente.CadastrarPendenteAsync();

    private Task<HttpResponseMessage> Enrolar(string? token, byte[]? imagem = null, string campo = "image") =>
        _cliente.EnviarFormularioAsync("/v1/auth/face/enroll", token, imagem, campo, imagem is null ? [("outro", "x")] : []);

    private static readonly byte[] Foto = ClienteDeTeste.Foto;

    private static async Task<string?> Code(HttpResponseMessage resposta)
    {
        using var doc = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private Task<HttpResponseMessage> Login(string cpf) =>
        _cliente.PostAsJsonAsync("/v1/auth/login/senha", new { cpf, senha = ClienteDeTeste.Senha }, ClienteDeTeste.Json);

    [Fact]
    public async Task Cadastro_pendente_nao_entra_ate_o_rosto_e_depois_entra_com_R5000_e_o_template_cifrado()
    {
        var (cpf, cadastro) = await PendenteAsync();
        Assert.Equal("CAPTURA_FACIAL", cadastro.ProximoPasso);
        Assert.Null(cadastro.Sessao);
        Assert.NotNull(cadastro.CadastroToken);
        Assert.True(cadastro.ExpiraEm > DateTimeOffset.UtcNow);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(cpf)).StatusCode); // pendente nem por senha

        var resposta = await Enrolar(cadastro.CadastroToken, Foto);
        var sessao = await resposta.Ler<SessaoResponse>();

        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        Assert.Equal("FACE", sessao.Metodo);
        Assert.Equal(Foto, _fabrica.Facial.UltimaImagem);
        var perfil = await (await _cliente.Enviar(HttpMethod.Get, "/v1/me", sessao)).Ler<PerfilResponse>();
        Assert.True(perfil.TemRosto);
        Assert.Equal(HttpStatusCode.OK, (await Login(cpf)).StatusCode); // agora a senha também vale

        await using var escopo = _fabrica.Services.CreateAsyncScope();
        var template = await escopo.ServiceProvider.GetRequiredService<MinisterioDbContext>().Templates.SingleAsync(t => t.UsuarioId == cadastro.UsuarioId);
        Assert.Equal(528, template.EmbeddingCifrado.Length);
        Assert.NotEqual(ClienteFacialFalso.Vetor, template.EmbeddingCifrado[..512]);
        var cifra = escopo.ServiceProvider.GetRequiredService<CifraDeTemplate>();
        Assert.Equal(ClienteFacialFalso.Vetor, cifra.Decifrar(cadastro.UsuarioId, template.EmbeddingCifrado, template.Nonce));
    }

    [Theory]
    [InlineData("rosto", "NO_FACE_DETECTED", HttpStatusCode.UnprocessableEntity)]
    [InlineData("varios", "MULTIPLE_FACES", HttpStatusCode.UnprocessableEntity)]
    public async Task Foto_sem_rosto_ou_com_varios_nao_gasta_o_cadastro_e_a_proxima_foto_conclui(string tipo, string code, HttpStatusCode status)
    {
        var (_, cadastro) = await PendenteAsync();
        _fabrica.Facial.Falha = tipo == "rosto" ? new RostoNaoDetectadoException() : new VariosRostosException();

        var recusada = await Enrolar(cadastro.CadastroToken, Foto);
        _fabrica.Facial.Falha = null;
        var aceita = await Enrolar(cadastro.CadastroToken, Foto);

        Assert.Equal(status, recusada.StatusCode);
        Assert.Equal(code, await Code(recusada));
        Assert.Equal(HttpStatusCode.Created, aceita.StatusCode);
    }

    [Fact]
    public async Task Servico_facial_fora_do_ar_devolve_503_com_Retry_After_e_o_cadastro_continua_pendente()
    {
        var (cpf, cadastro) = await PendenteAsync();
        _fabrica.Facial.Falha = new ServicoFacialIndisponivelException();

        var resposta = await Enrolar(cadastro.CadastroToken, Foto);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, resposta.StatusCode);
        Assert.Equal("FACE_SERVICE_UNAVAILABLE", await Code(resposta));
        Assert.True(resposta.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(cpf)).StatusCode); // sem rosto, nada de conta
    }

    [Fact]
    public async Task Cadastro_concluido_nao_aceita_outro_rosto()
    {
        var (_, cadastro) = await PendenteAsync();
        Assert.Equal(HttpStatusCode.Created, (await Enrolar(cadastro.CadastroToken, Foto)).StatusCode);

        var segunda = await Enrolar(cadastro.CadastroToken, Foto);

        Assert.Equal(HttpStatusCode.Unauthorized, segunda.StatusCode);
        Assert.Equal("FACE_TOKEN_EXPIRED", await Code(segunda));
    }

    [Fact]
    public async Task Fotos_simultaneas_do_mesmo_cadastro_concluem_uma_vez()
    {
        var (_, cadastro) = await PendenteAsync();

        var respostas = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Enrolar(cadastro.CadastroToken, Foto)));

        Assert.Equal(1, respostas.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.All(respostas.Where(r => r.StatusCode != HttpStatusCode.Created), r => Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode));
        var sessao = await respostas.Single(r => r.StatusCode == HttpStatusCode.Created).Ler<SessaoResponse>();
        Assert.True((await (await _cliente.Enviar(HttpMethod.Get, "/v1/me", sessao)).Ler<PerfilResponse>()).TemRosto);
    }

    [Fact]
    public async Task Token_de_cadastro_so_serve_para_o_rosto_e_token_de_sessao_nao_serve_para_o_rosto()
    {
        var (_, cadastro) = await PendenteAsync();
        var comToken = new HttpRequestMessage(HttpMethod.Get, "/v1/me");
        comToken.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cadastro.CadastroToken);
        var contaComTokenDeCadastro = await _cliente.SendAsync(comToken);

        var outro = await PendenteAsync();
        var sessao = await (await Enrolar(outro.Cadastro.CadastroToken, Foto)).Ler<SessaoResponse>();
        var rostoComSessao = await Enrolar(sessao.AccessToken, Foto);

        Assert.Equal(HttpStatusCode.Forbidden, contaComTokenDeCadastro.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, rostoComSessao.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Enrolar(null, Foto)).StatusCode);
    }

    [Fact]
    public async Task Envio_sem_imagem_ou_grande_demais_e_recusado_antes_do_servico_facial()
    {
        var (_, cadastro) = await PendenteAsync();
        var chamadasAntes = _fabrica.Facial.Chamadas;

        var semImagem = await Enrolar(cadastro.CadastroToken);
        var campoErrado = await Enrolar(cadastro.CadastroToken, Foto, campo: "foto");
        var grande = await Enrolar(cadastro.CadastroToken, new byte[(2 * 1024 * 1024) + 1]);

        Assert.Equal(HttpStatusCode.BadRequest, semImagem.StatusCode);
        Assert.Equal("INVALID_IMAGE", await Code(semImagem));
        Assert.Equal(HttpStatusCode.BadRequest, campoErrado.StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, grande.StatusCode);
        Assert.Equal("IMAGE_TOO_LARGE", await Code(grande));
        Assert.Equal(chamadasAntes, _fabrica.Facial.Chamadas);
    }
}
