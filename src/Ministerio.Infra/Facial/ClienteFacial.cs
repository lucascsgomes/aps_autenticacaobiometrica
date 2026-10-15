using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ministerio.Dominio;
using Ministerio.Dominio.Biometria;
using Microsoft.Extensions.Logging;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace Ministerio.Infra.Facial;

/// <summary>O score existe para auditoria interna. Nenhuma resposta da API o repete: score na mão do cliente vira oráculo.</summary>
public sealed record ResultadoDaVerificacao(bool Corresponde, double Score);

/// <summary>Uma pessoa cadastrada contra quem a foto será comparada na identificação 1 para N. <c>Referencia</c> é o vetor já decifrado.</summary>
public sealed record Candidato(string Id, byte[] Referencia);

/// <summary>
/// <c>CandidatoId</c> só vem preenchido quando há um vencedor claro. O score existe só para auditoria interna e nunca vai ao cliente.
/// </summary>
public sealed record ResultadoDaIdentificacao(bool Identificado, string? CandidatoId, double Score, bool Ambiguo);

/// <summary>Fala com o face-service. Sem estado e sem guardar nada: a imagem só passa.</summary>
public interface IClienteFacial
{
    /// <summary>Devolve o vetor facial (128 floats de 32 bits, 512 bytes). Lança as exceções de negócio do rosto ou <see cref="ServicoFacialIndisponivelException"/>.</summary>
    Task<byte[]> CadastrarAsync(byte[] imagem, string? idDaRequisicao, CancellationToken cancelamento = default);

    /// <summary>Compara a foto com o vetor de referência (já decifrado) no limiar dado. Mesmas exceções de <see cref="CadastrarAsync"/>.</summary>
    Task<ResultadoDaVerificacao> VerificarAsync(byte[] imagem, byte[] referencia, decimal limiar, string? idDaRequisicao, CancellationToken cancelamento = default);

    /// <summary>1 para N: descobre quem é entre os candidatos. Recusa quando ninguém passa do limiar ou quando dois ficam parecidos demais (<paramref name="margem"/>).</summary>
    Task<ResultadoDaIdentificacao> IdentificarAsync(byte[] imagem, IReadOnlyList<Candidato> candidatos, decimal limiar, decimal margem, string? idDaRequisicao, CancellationToken cancelamento = default);
}

public sealed class OpcoesFaciais
{
    public const string Secao = "Face";

    public string? BaseUrl { get; set; }
    public string? ChaveInterna { get; set; }
}

/// <summary>Registrado quando o face-service não está configurado: o rosto fica indisponível em vez de ser aceito sem verificação.</summary>
internal sealed class ClienteFacialNaoConfigurado : IClienteFacial
{
    public Task<byte[]> CadastrarAsync(byte[] imagem, string? idDaRequisicao, CancellationToken cancelamento = default) =>
        throw new ServicoFacialIndisponivelException();

    public Task<ResultadoDaVerificacao> VerificarAsync(byte[] imagem, byte[] referencia, decimal limiar, string? idDaRequisicao, CancellationToken cancelamento = default) =>
        throw new ServicoFacialIndisponivelException();

    public Task<ResultadoDaIdentificacao> IdentificarAsync(byte[] imagem, IReadOnlyList<Candidato> candidatos, decimal limiar, decimal margem, string? idDaRequisicao, CancellationToken cancelamento = default) =>
        throw new ServicoFacialIndisponivelException();
}

public sealed partial class ClienteFacialHttp(HttpClient http, ILogger<ClienteFacialHttp> logger) : IClienteFacial
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public async Task<byte[]> CadastrarAsync(byte[] imagem, string? idDaRequisicao, CancellationToken cancelamento = default)
    {
        using var conteudo = new MultipartFormDataContent { { Imagem(imagem), "image", "rosto" } };
        using var resposta = await PostarAsync("internal/v1/face/enroll", conteudo, idDaRequisicao, cancelamento);

        var corpo = await resposta.Content.ReadFromJsonAsync<RespostaDeEnroll>(Json, cancelamento);
        try
        {
            var bytes = Convert.FromBase64String(corpo?.EmbeddingB64 ?? string.Empty);
            if (bytes.Length == TemplateFacial.Dimensao * sizeof(float) && corpo!.Dim == TemplateFacial.Dimensao)
            {
                return bytes;
            }
        }
        catch (FormatException)
        {
            // cai no erro abaixo
        }

        LogarRecusa(logger, (int)resposta.StatusCode, "EMBEDDING_INVALIDO");
        throw new ServicoFacialIndisponivelException();
    }

    public async Task<ResultadoDaVerificacao> VerificarAsync(byte[] imagem, byte[] referencia, decimal limiar, string? idDaRequisicao, CancellationToken cancelamento = default)
    {
        using var conteudo = new MultipartFormDataContent
        {
            { Imagem(imagem), "image", "rosto" },
            { new StringContent(Convert.ToBase64String(referencia)), "reference_b64" },
            { new StringContent(limiar.ToString(CultureInfo.InvariantCulture)), "threshold" },
        };
        using var resposta = await PostarAsync("internal/v1/face/verify", conteudo, idDaRequisicao, cancelamento);

        var corpo = await resposta.Content.ReadFromJsonAsync<RespostaDeVerify>(Json, cancelamento);
        if (corpo is null)
        {
            LogarRecusa(logger, (int)resposta.StatusCode, "VERIFY_INVALIDO");
            throw new ServicoFacialIndisponivelException();
        }

        return new ResultadoDaVerificacao(corpo.Match, corpo.Score);
    }

    public async Task<ResultadoDaIdentificacao> IdentificarAsync(byte[] imagem, IReadOnlyList<Candidato> candidatos, decimal limiar, decimal margem, string? idDaRequisicao, CancellationToken cancelamento = default)
    {
        var lista = candidatos.Select(c => new { id = c.Id, embedding_b64 = Convert.ToBase64String(c.Referencia) });
        using var conteudo = new MultipartFormDataContent
        {
            { Imagem(imagem), "image", "rosto" },
            { new StringContent(JsonSerializer.Serialize(lista)), "candidates" },
            { new StringContent(limiar.ToString(CultureInfo.InvariantCulture)), "threshold" },
            { new StringContent(margem.ToString(CultureInfo.InvariantCulture)), "margin" },
        };
        using var resposta = await PostarAsync("internal/v1/face/identify", conteudo, idDaRequisicao, cancelamento);

        var corpo = await resposta.Content.ReadFromJsonAsync<RespostaDeIdentify>(Json, cancelamento);
        if (corpo is null)
        {
            LogarRecusa(logger, (int)resposta.StatusCode, "IDENTIFY_INVALIDO");
            throw new ServicoFacialIndisponivelException();
        }

        return new ResultadoDaIdentificacao(corpo.Identified, corpo.CandidateId, corpo.Score, corpo.Ambiguous);
    }

    private static ByteArrayContent Imagem(byte[] bytes)
    {
        var arquivo = new ByteArrayContent(bytes);
        arquivo.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return arquivo;
    }

    /// <summary>Envia e devolve só respostas de sucesso. O resto vira a exceção de negócio certa.</summary>
    private async Task<HttpResponseMessage> PostarAsync(string rota, HttpContent conteudo, string? idDaRequisicao, CancellationToken cancelamento)
    {
        using var requisicao = new HttpRequestMessage(HttpMethod.Post, rota) { Content = conteudo };
        if (idDaRequisicao is not null)
        {
            requisicao.Headers.Add("X-Request-Id", idDaRequisicao);
        }

        HttpResponseMessage resposta;
        try
        {
            resposta = await http.SendAsync(requisicao, cancelamento);
        }
        catch (Exception e) when (!cancelamento.IsCancellationRequested
            && e is HttpRequestException or OperationCanceledException or BrokenCircuitException or TimeoutRejectedException)
        {
            LogarIndisponivel(logger, e.GetType().Name);
            throw new ServicoFacialIndisponivelException();
        }

        if (resposta.IsSuccessStatusCode)
        {
            return resposta;
        }

        using (resposta)
        {
            var codigo = await LerCodigoAsync(resposta, cancelamento);
            switch (codigo)
            {
                case "NO_FACE_DETECTED":
                    throw new RostoNaoDetectadoException();
                case "MULTIPLE_FACES":
                    throw new VariosRostosException();
                case "INVALID_IMAGE":
                    throw new ImagemInvalidaException();
                case "IMAGE_TOO_LARGE":
                    throw new ImagemGrandeException();
                case "INVALID_REFERENCE" or "INVALID_THRESHOLD" or "INVALID_CANDIDATES" or "INVALID_MARGIN":
                    // Bug nosso ou template corrompido: não é culpa do usuário nem do serviço. Vira 500 e alerta.
                    LogarRecusa(logger, (int)resposta.StatusCode, codigo);
                    throw new InvalidOperationException($"face-service recusou a referência ou o limiar enviados ({codigo}).");
                default:
                    // Chave interna errada, modelos fora, 5xx, resposta que não entendemos: tudo é serviço indisponível.
                    LogarRecusa(logger, (int)resposta.StatusCode, codigo ?? "?");
                    throw new ServicoFacialIndisponivelException();
            }
        }
    }

    private static async Task<string?> LerCodigoAsync(HttpResponseMessage resposta, CancellationToken cancelamento)
    {
        if (resposta.StatusCode is HttpStatusCode.Unauthorized or >= HttpStatusCode.InternalServerError)
        {
            return null;
        }

        try
        {
            return (await resposta.Content.ReadFromJsonAsync<RespostaDeErro>(Json, cancelamento))?.Code;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record RespostaDeEnroll([property: JsonPropertyName("embedding_b64")] string? EmbeddingB64, int Dim);

    private sealed record RespostaDeVerify(bool Match, double Score);

    private sealed record RespostaDeIdentify(bool Identified, string? CandidateId, double Score, bool Ambiguous);

    private sealed record RespostaDeErro(string? Code);

    [LoggerMessage(Level = LogLevel.Warning, Message = "face-service indisponível ({Motivo}).")]
    private static partial void LogarIndisponivel(ILogger logger, string motivo);

    [LoggerMessage(Level = LogLevel.Error, Message = "face-service recusou a chamada: status {Status}, code {Codigo}.")]
    private static partial void LogarRecusa(ILogger logger, int status, string codigo);
}
