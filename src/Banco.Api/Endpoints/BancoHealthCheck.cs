using Banco.Infra.Persistencia;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Banco.Api.Endpoints;

/// <summary>Pronto = o PostgreSQL responde. Nunca expõe host, usuário ou senha na resposta.</summary>
internal sealed class BancoHealthCheck(BancoDbContext banco) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            return await banco.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("Banco de dados inacessível.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return HealthCheckResult.Unhealthy("Banco de dados inacessível.");
        }
    }
}
