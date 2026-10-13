using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Banco.Api.Seguranca;

/// <summary>Rate limit em janela fixa de 1 minuto. Rotas autenticadas contam por usuário; as demais, por IP.</summary>
internal static class Limites
{
    public const string Publico = "publico";
    public const string Biometria = "biometria";
    public const string ConsultaChave = "consulta-chave";
    public const string Leitura = "leitura";
    public const string Transferencia = "transferencia";

    private static readonly TimeSpan Janela = TimeSpan.FromMinutes(1);

    /// <summary>
    /// <c>RateLimit:Fator</c> multiplica todos os limites (os testes de integração sobem para não esbarrar neles).
    /// Atrás de proxy (Render), defina ASPNETCORE_FORWARDEDHEADERS_ENABLED=true, senão todo mundo chega com o IP do proxy.
    /// </summary>
    public static IServiceCollection AdicionarLimites(this IServiceCollection servicos, IConfiguration configuracao)
    {
        var fator = configuracao.GetValue("RateLimit:Fator", 1);
        return servicos.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.AddPolicy(Publico, c => PorIp(c, 20 * fator));
            o.AddPolicy(Biometria, c => PorIp(c, 5 * fator));
            o.AddPolicy(ConsultaChave, c => PorUsuario(c, 10 * fator));
            o.AddPolicy(Transferencia, c => PorUsuario(c, 10 * fator));
            o.AddPolicy(Leitura, c => PorUsuario(c, 120 * fator));
            o.OnRejected = async (contexto, ct) =>
            {
                var http = contexto.HttpContext;
                if (contexto.Lease.TryGetMetadata(MetadataName.RetryAfter, out var espera))
                {
                    http.Response.Headers.RetryAfter = Math.Ceiling(espera.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                }

                var problema = new ProblemDetails
                {
                    Status = StatusCodes.Status429TooManyRequests,
                    Title = "Muitas requisições",
                    Detail = "Você passou do limite de requisições. Tente de novo em instantes.",
                    Type = "https://httpstatuses.com/429",
                };
                problema.Extensions["code"] = "RATE_LIMITED";
                problema.Extensions["traceId"] = http.TraceIdentifier;
                await Results.Problem(problema).ExecuteAsync(http);
            };
        });
    }

    private static RateLimitPartition<string> PorIp(HttpContext c, int limite) =>
        Particao($"ip:{c.Connection.RemoteIpAddress}", limite);

    private static RateLimitPartition<string> PorUsuario(HttpContext c, int limite) =>
        Particao(c.User.FindFirstValue(JwtRegisteredClaimNames.Sub) is { } sub ? $"u:{sub}" : $"ip:{c.Connection.RemoteIpAddress}", limite);

    private static RateLimitPartition<string> Particao(string chave, int limite) =>
        RateLimitPartition.GetFixedWindowLimiter(chave, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = limite,
            Window = Janela,
            QueueLimit = 0,
        });
}
