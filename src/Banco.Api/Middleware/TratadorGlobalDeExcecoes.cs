using Banco.Dominio;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Banco.Api.Middleware;

/// <summary>
/// Converte qualquer exceção não tratada em Problem Details (RFC 9457), sem vazar detalhes internos.
/// </summary>
internal sealed partial class TratadorGlobalDeExcecoes(
    ILogger<TratadorGlobalDeExcecoes> logger,
    IProblemDetailsService problemas) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext contexto, Exception excecao, CancellationToken ct)
    {
        if (excecao is ContaBloqueadaException bloqueada)
        {
            contexto.Response.Headers.RetryAfter = Math.Ceiling(bloqueada.TentarEm.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (excecao is ServicoFacialIndisponivelException)
        {
            contexto.Response.Headers.RetryAfter = "15";
        }

        // O Kestrel recusa corpo acima do limite da rota: vira o mesmo erro de imagem grande, não um 500.
        if (excecao is BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge })
        {
            excecao = new ImagemGrandeException();
        }

        var problema = excecao is NegocioException negocio
            ? MontarProblema(negocio)
            : ProblemaInterno(excecao, contexto);
        problema.Extensions["traceId"] = contexto.TraceIdentifier;

        contexto.Response.StatusCode = problema.Status!.Value;
        return await problemas.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = contexto,
            ProblemDetails = problema,
            Exception = excecao,
        });
    }

    /// <summary>Monta o Problem Details de uma falha de negócio. Público na API porque a idempotência guarda e repete a mesma resposta.</summary>
    internal static ProblemDetails MontarProblema(NegocioException negocio)
    {
        var (status, titulo) = negocio switch
        {
            ConflitoDeCadastroException => (StatusCodes.Status409Conflict, "Cadastro não concluído"),
            CredenciaisInvalidasException => (StatusCodes.Status401Unauthorized, "Credenciais inválidas"),
            ContaBloqueadaException => (StatusCodes.Status403Forbidden, "Conta bloqueada"),
            ConfirmacaoNecessariaException => (StatusCodes.Status403Forbidden, "Confirme com o rosto ou a senha"),
            NaoAutenticadoException => (StatusCodes.Status401Unauthorized, "Não autenticado"),
            TransferenciaNaoEncontradaException => (StatusCodes.Status404NotFound, "Transferência não encontrada"),
            MesmaContaException or DestinoIndisponivelException or SaldoInsuficienteException or ChaveDeIdempotenciaReusadaException
                or LimiteNoturnoExcedidoException or LimiteDiurnoExcedidoException
                => (StatusCodes.Status422UnprocessableEntity, "Transferência recusada"),
            SenhaNaoVerificadaException => (StatusCodes.Status401Unauthorized, "Senha não confere"),
            TransferenciaNaoPendenteException or TransferenciaExpiradaException or RequisicaoEmAndamentoException
                => (StatusCodes.Status409Conflict, "Transferência não pode ser confirmada"),
            ChaveDeIdempotenciaObrigatoriaException => (StatusCodes.Status400BadRequest, "Idempotency-Key obrigatória"),
            RostoNaoDetectadoException or VariosRostosException => (StatusCodes.Status422UnprocessableEntity, "Rosto não reconhecido na foto"),
            ImagemInvalidaException => (StatusCodes.Status400BadRequest, "Imagem inválida"),
            ImagemGrandeException => (StatusCodes.Status413PayloadTooLarge, "Imagem grande demais"),
            ServicoFacialIndisponivelException => (StatusCodes.Status503ServiceUnavailable, "Reconhecimento facial indisponível"),
            FaceNaoVerificadaException => (StatusCodes.Status401Unauthorized, "Rosto não confirmado"),
            TokenDeCadastroEncerradoException => (StatusCodes.Status401Unauthorized, "Cadastro encerrado"),
            ChaveNaoEncontradaException => (StatusCodes.Status404NotFound, "Chave não encontrada"),
            ChaveInvalidaException => (StatusCodes.Status422UnprocessableEntity, "Chave inválida"),
            ChaveJaExisteException => (StatusCodes.Status409Conflict, "Chave já existe"),
            LimiteDeChavesException => (StatusCodes.Status422UnprocessableEntity, "Limite de chaves"),
            _ => (StatusCodes.Status400BadRequest, "Requisição inválida"),
        };

        var problema = new ProblemDetails { Status = status, Title = titulo, Detail = negocio.Message, Type = $"https://httpstatuses.com/{status}" };
        problema.Extensions["code"] = negocio.Code;
        if (negocio is ConfirmacaoNecessariaException confirmacao)
        {
            problema.Extensions["finalidade"] = confirmacao.Finalidade;
        }

        return problema;
    }

    private ProblemDetails ProblemaInterno(Exception excecao, HttpContext contexto)
    {
        LogarFalha(logger, contexto.TraceIdentifier, excecao);
        var problema = new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "Erro interno.",
            Detail = "Não foi possível concluir a operação. Tente novamente em instantes.",
            Type = "https://httpstatuses.com/500",
        };
        problema.Extensions["code"] = "INTERNAL_ERROR";
        return problema;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Exceção não tratada. traceId={TraceId}")]
    private static partial void LogarFalha(ILogger logger, string traceId, Exception excecao);
}
