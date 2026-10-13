using Microsoft.Extensions.DependencyInjection;

namespace Banco.Web.Servicos;

public static class ServicosWeb
{
    /// <summary>Registra o cliente da API e a sessão. <paramref name="api"/> é o endereço base da API do banco.</summary>
    public static IServiceCollection AdicionarBancoWeb(this IServiceCollection servicos, Uri api) =>
        servicos
            .AddScoped(_ => new HttpClient { BaseAddress = api })
            .AddScoped<ModuloJs>()
            .AddScoped<SessaoApp>()
            .AddScoped<ApiBanco>();
}
