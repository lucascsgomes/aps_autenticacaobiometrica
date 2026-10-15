using Microsoft.Extensions.DependencyInjection;

namespace Ministerio.Web.Servicos;

public static class ServicosWeb
{
    /// <summary>Registra o cliente da API e a sessão. <paramref name="api"/> é o endereço base da API do sistema.</summary>
    public static IServiceCollection AdicionarMinisterioWeb(this IServiceCollection servicos, Uri api) =>
        servicos
            .AddScoped(_ => new HttpClient { BaseAddress = api })
            .AddScoped<ModuloJs>()
            .AddScoped<SessaoApp>()
            .AddScoped<ApiMinisterio>();
}
