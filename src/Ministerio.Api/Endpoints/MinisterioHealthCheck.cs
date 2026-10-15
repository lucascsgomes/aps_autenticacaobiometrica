using Ministerio.Infra.Persistencia;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Ministerio.Api.Endpoints;

/// <summary>Pronto = o PostgreSQL responde. Nunca expõe host, usuário ou senha na resposta.</summary>
internal sealed class MinisterioHealthCheck(MinisterioDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            return await db.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("Ministerio de dados inacessível.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return HealthCheckResult.Unhealthy("Ministerio de dados inacessível.");
        }
    }
}
