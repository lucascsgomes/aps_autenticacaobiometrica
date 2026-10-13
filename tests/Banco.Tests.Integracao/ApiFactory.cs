using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Banco.Tests.Integracao;

/// <summary>Sobe a API inteira em memória. Registra uma rota que sempre falha para testar o tratador de erros.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string RotaQueFalha = "/__teste/falha";

    // Sempre sobrescreve a conexão: sem isso os user-secrets de quem roda os testes apontariam para o Neon.
    private readonly string _conexao = ServidorPostgres.CriarBancoAsync().GetAwaiter().GetResult();

    public string? ConexaoForcada { get; init; }

    public const string ChaveJwt = "chave-de-teste-com-mais-de-trinta-e-dois-bytes-123456";

    /// <summary>Liga o cadastro sem rosto (modo de desenvolvimento). Desligado, o cadastro fica pendente.</summary>
    public bool CadastroSemRosto { get; init; } = true;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Banco", ConexaoForcada ?? _conexao);
        builder.UseSetting("Jwt:Chave", ChaveJwt);
        builder.UseSetting("Auth:CadastroSemRosto", CadastroSemRosto ? "true" : "false");
        builder.ConfigureServices(s => s.AddTransient<IStartupFilter, FiltroDeRotaQueFalha>());
    }

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
