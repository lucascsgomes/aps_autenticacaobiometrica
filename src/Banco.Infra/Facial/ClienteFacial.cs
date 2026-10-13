using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Banco.Dominio;
using Banco.Dominio.Biometria;
using Microsoft.Extensions.Logging;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace Banco.Infra.Facial;

/// <summary>Fala com o face-service. Sem estado e sem guardar nada: a imagem só passa.</summary>
public interface IClienteFacial
{
    /// <summary>Devolve o vetor facial (128 floats de 32 bits, 512 bytes). Lança as exceções de negócio do rosto ou <see cref="ServicoFacialIndisponivelException"/>.</summary>
    Task<byte[]> CadastrarAsync(byte[] imagem, string? idDaRequisicao, CancellationToken cancelamento = default);
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
}

public sealed partial class ClienteFacialHttp(HttpClient http, ILogger<ClienteFacialHttp> logger) : IClienteFacial
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public async Task<byte[]> CadastrarAsync(byte[] imagem, string? idDaRequisicao, CancellationToken cancelamento = default)
    {
        using var conteudo = new MultipartFormDataContent();
        var arquivo = new ByteArrayContent(imagem);
        arquivo.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        conteudo.Add(arquivo, "image", "rosto");
        using var requisicao = new HttpRequestMessage(HttpMethod.Post, "internal/v1/face/enroll") { Content = conteudo };
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

        using (resposta)
        {
            if (resposta.IsSuccessStatusCode)
            {
                return await LerEmbeddingAsync(resposta, cancelamento);
            }

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
                default:
                    // Chave interna errada, modelos fora, 5xx, resposta que não entendemos: tudo é serviço indisponível.
                    LogarRecusa(logger, (int)resposta.StatusCode, codigo ?? "?");
                    throw new ServicoFacialIndisponivelException();
            }
        }
    }

    private async Task<byte[]> LerEmbeddingAsync(HttpResponseMessage resposta, CancellationToken cancelamento)
    {
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

    private sealed record RespostaDeErro(string? Code);

    [LoggerMessage(Level = LogLevel.Warning, Message = "face-service indisponível ({Motivo}).")]
    private static partial void LogarIndisponivel(ILogger logger, string motivo);

    [LoggerMessage(Level = LogLevel.Error, Message = "face-service recusou a chamada: status {Status}, code {Codigo}.")]
    private static partial void LogarRecusa(ILogger logger, int status, string codigo);
}
