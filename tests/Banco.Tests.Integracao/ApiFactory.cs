using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Banco.Tests.Integracao;

/// <summary>Sobe a API inteira em memória. Registra uma rota que sempre falha para testar o tratador de erros.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string RotaQueFalha = "/__teste/falha";

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.ConfigureServices(s => s.AddTransient<IStartupFilter, FiltroDeRotaQueFalha>());

    private sealed class FiltroDeRotaQueFalha : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> proximo) => app =>
        {
            proximo(app);
            app.UseRouting();
            app.UseEndpoints(e => e.MapGet(RotaQueFalha, (Func<string>)(() => throw new InvalidOperationException("segredo interno"))));
        };
    }
}
