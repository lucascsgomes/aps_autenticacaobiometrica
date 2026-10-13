using System.Net;
using System.Security.Cryptography;
using System.Text;
using Banco.Dominio;
using Banco.Dominio.Biometria;
using Banco.Infra.Facial;
using Microsoft.Extensions.Logging.Abstractions;

namespace Banco.Tests;

public class CifraDeTemplateTests
{
    private static readonly string Chave = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private static byte[] Vetor() => RandomNumberGenerator.GetBytes(512);

    [Fact]
    public void Cifra_no_tamanho_do_banco_e_decifra_de_volta()
    {
        var cifra = new CifraDeTemplate(Chave, 1);
        var usuario = Guid.NewGuid();
        var vetor = Vetor();

        var (cifrado, nonce) = cifra.Cifrar(usuario, vetor);

        Assert.Equal(TemplateFacial.TamanhoCifrado, cifrado.Length);
        Assert.Equal(TemplateFacial.TamanhoNonce, nonce.Length);
        Assert.NotEqual(vetor, cifrado[..512]);
        Assert.Equal(vetor, cifra.Decifrar(usuario, cifrado, nonce));
    }

    [Fact]
    public void Template_copiado_para_outro_usuario_nao_decifra()
    {
        var cifra = new CifraDeTemplate(Chave, 1);
        var (cifrado, nonce) = cifra.Cifrar(Guid.NewGuid(), Vetor());

        Assert.ThrowsAny<CryptographicException>(() => cifra.Decifrar(Guid.NewGuid(), cifrado, nonce));
    }

    [Fact]
    public void Dado_adulterado_ou_chave_diferente_nao_decifra()
    {
        var cifra = new CifraDeTemplate(Chave, 1);
        var usuario = Guid.NewGuid();
        var (cifrado, nonce) = cifra.Cifrar(usuario, Vetor());
        var adulterado = (byte[])cifrado.Clone();
        adulterado[10] ^= 1;

        Assert.ThrowsAny<CryptographicException>(() => cifra.Decifrar(usuario, adulterado, nonce));
        Assert.ThrowsAny<CryptographicException>(() => new CifraDeTemplate(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)), 1).Decifrar(usuario, cifrado, nonce));
    }

    [Fact]
    public void Cada_cifragem_usa_um_nonce_novo()
    {
        var cifra = new CifraDeTemplate(Chave, 1);
        var usuario = Guid.NewGuid();
        var vetor = Vetor();

        Assert.NotEqual(cifra.Cifrar(usuario, vetor).Nonce, cifra.Cifrar(usuario, vetor).Nonce);
    }

    [Fact]
    public void Recusa_chave_que_nao_tem_32_bytes_em_base64()
    {
        Assert.Throws<InvalidOperationException>(() => new CifraDeTemplate(Convert.ToBase64String(new byte[16]), 1));
        Assert.Throws<InvalidOperationException>(() => new CifraDeTemplate("isto não é base64", 1));
    }

    [Fact]
    public void Sem_chave_nao_guarda_template()
    {
        var cifra = new CifraDeTemplate(null, 1);

        Assert.False(cifra.Configurada);
        Assert.Throws<InvalidOperationException>(() => cifra.Cifrar(Guid.NewGuid(), Vetor()));
    }
}

public class ClienteFacialHttpTests
{
    private sealed class Falso(Func<HttpRequestMessage, Task<HttpResponseMessage>> resposta) : HttpMessageHandler
    {
        public HttpRequestMessage? Requisicao { get; private set; }

        public string? CampoDoArquivo { get; private set; }

        public Dictionary<string, string> Campos { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requisicao = request;
            var partes = await ((MultipartFormDataContent)request.Content!).First().ReadAsByteArrayAsync(cancellationToken);
            CampoDoArquivo = ((MultipartFormDataContent)request.Content!).First().Headers.ContentDisposition?.Name?.Trim('"') + ":" + partes.Length;
            foreach (var parte in (MultipartFormDataContent)request.Content!)
            {
                Campos[parte.Headers.ContentDisposition!.Name!.Trim('"')] = await parte.ReadAsStringAsync(cancellationToken);
            }

            return await resposta(request);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string corpo) =>
        new(status) { Content = new StringContent(corpo, Encoding.UTF8, "application/json") };

    private static string Embedding(int bytes = 512) => $$"""{"embedding_b64":"{{Convert.ToBase64String(new byte[bytes])}}","dim":128,"request_id":"r"}""";

    private static ClienteFacialHttp Cliente(Falso falso) =>
        new(new HttpClient(falso) { BaseAddress = new Uri("http://face.local/") }, NullLogger<ClienteFacialHttp>.Instance);

    [Fact]
    public async Task Envia_a_imagem_no_campo_image_com_o_id_da_requisicao_e_devolve_o_vetor()
    {
        var falso = new Falso(_ => Task.FromResult(Json(HttpStatusCode.OK, Embedding())));

        var vetor = await Cliente(falso).CadastrarAsync([1, 2, 3], "req-1");

        Assert.Equal(512, vetor.Length);
        Assert.Equal("http://face.local/internal/v1/face/enroll", falso.Requisicao!.RequestUri!.ToString());
        Assert.Equal("req-1", falso.Requisicao.Headers.GetValues("X-Request-Id").Single());
        Assert.Equal("image:3", falso.CampoDoArquivo);
    }

    [Theory]
    [InlineData(HttpStatusCode.UnprocessableEntity, "NO_FACE_DETECTED", typeof(RostoNaoDetectadoException))]
    [InlineData(HttpStatusCode.UnprocessableEntity, "MULTIPLE_FACES", typeof(VariosRostosException))]
    [InlineData(HttpStatusCode.BadRequest, "INVALID_IMAGE", typeof(ImagemInvalidaException))]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, "IMAGE_TOO_LARGE", typeof(ImagemGrandeException))]
    [InlineData(HttpStatusCode.Unauthorized, "UNAUTHORIZED", typeof(ServicoFacialIndisponivelException))]
    [InlineData(HttpStatusCode.ServiceUnavailable, "MODELS_NOT_LOADED", typeof(ServicoFacialIndisponivelException))]
    [InlineData(HttpStatusCode.InternalServerError, "INTERNAL_ERROR", typeof(ServicoFacialIndisponivelException))]
    [InlineData(HttpStatusCode.BadRequest, "INVALID_REQUEST", typeof(ServicoFacialIndisponivelException))]
    public async Task Traduz_o_erro_do_servico_facial(HttpStatusCode status, string code, Type esperado)
    {
        var falso = new Falso(_ => Task.FromResult(Json(status, $$"""{"code":"{{code}}"}""")));

        var erro = await Assert.ThrowsAnyAsync<NegocioException>(() => Cliente(falso).CadastrarAsync([1], null));

        Assert.IsType(esperado, erro);
    }

    [Fact]
    public async Task Conexao_recusada_e_resposta_estranha_viram_servico_indisponivel()
    {
        var recusada = new Falso(_ => throw new HttpRequestException("recusada"));
        var vetorCurto = new Falso(_ => Task.FromResult(Json(HttpStatusCode.OK, Embedding(100))));
        var semJson = new Falso(_ => Task.FromResult(Json(HttpStatusCode.OK, "{}")));

        await Assert.ThrowsAsync<ServicoFacialIndisponivelException>(() => Cliente(recusada).CadastrarAsync([1], null));
        await Assert.ThrowsAsync<ServicoFacialIndisponivelException>(() => Cliente(vetorCurto).CadastrarAsync([1], null));
        await Assert.ThrowsAsync<ServicoFacialIndisponivelException>(() => Cliente(semJson).CadastrarAsync([1], null));
    }

    [Fact]
    public async Task Verificar_envia_foto_referencia_e_limiar_e_devolve_o_resultado()
    {
        var falso = new Falso(_ => Task.FromResult(Json(HttpStatusCode.OK, """{"match":true,"score":0.71,"threshold":0.363,"request_id":"r"}""")));
        var referencia = new byte[512];
        referencia[0] = 7;

        var resultado = await Cliente(falso).VerificarAsync([1, 2], referencia, 0.363m, "req-2");

        Assert.True(resultado.Corresponde);
        Assert.Equal(0.71, resultado.Score);
        Assert.Equal("http://face.local/internal/v1/face/verify", falso.Requisicao!.RequestUri!.ToString());
        Assert.Equal(Convert.ToBase64String(referencia), falso.Campos["reference_b64"]);
        Assert.Equal("0.363", falso.Campos["threshold"]); // ponto decimal, em qualquer cultura do servidor
    }

    [Theory]
    [InlineData("INVALID_REFERENCE")]
    [InlineData("INVALID_THRESHOLD")]
    public async Task Referencia_ou_limiar_recusados_sao_bug_nosso_e_viram_erro_interno_nao_servico_indisponivel(string code)
    {
        var falso = new Falso(_ => Task.FromResult(Json(HttpStatusCode.BadRequest, $$"""{"code":"{{code}}"}""")));

        await Assert.ThrowsAsync<InvalidOperationException>(() => Cliente(falso).VerificarAsync([1], new byte[512], 0.363m, null));
    }
}
