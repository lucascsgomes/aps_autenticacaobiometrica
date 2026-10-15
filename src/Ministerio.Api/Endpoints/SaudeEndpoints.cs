using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace Ministerio.Api.Endpoints;

internal static class SaudeEndpoints
{
    public const string TagProntidao = "ready";

    /// <summary>
    /// /health: o processo está de pé (liveness), sem checar dependências.
    /// /ready: dependências críticas respondem (db e face-service entram nas próximas ondas).
    /// </summary>
    public static IEndpointRouteBuilder MapearSaude(this IEndpointRouteBuilder rotas)
    {
        rotas.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false });
        rotas.MapHealthChecks("/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains(TagProntidao) });
        return rotas;
    }
}
