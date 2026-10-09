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
        LogarFalha(logger, contexto.TraceIdentifier, excecao);

        var problema = new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "Erro interno.",
            Detail = "Não foi possível concluir a operação. Tente novamente em instantes.",
            Type = "https://httpstatuses.com/500",
        };
        problema.Extensions["traceId"] = contexto.TraceIdentifier;

        contexto.Response.StatusCode = problema.Status.Value;
        return await problemas.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = contexto,
            ProblemDetails = problema,
            Exception = excecao,
        });
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Exceção não tratada. traceId={TraceId}")]
    private static partial void LogarFalha(ILogger logger, string traceId, Exception excecao);
}
