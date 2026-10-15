using Ministerio.Infra.Facial;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Ministerio.Tests.Integracao;

/// <summary>Sobe a API inteira em memória. Registra uma rota que sempre falha para testar o tratador de erros.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string RotaQueFalha = "/__teste/falha";

    // Sempre sobrescreve a conexão: sem isso os user-secrets de quem roda os testes apontariam para o Neon.
    private readonly string _conexao = ServidorPostgres.CriarBaseAsync().GetAwaiter().GetResult();

    /// <summary>O face-service de mentira usado por esta fábrica. O teste manda a falha que quiser.</summary>
    public ClienteFacialFalso Facial { get; } = new();

    public string? ConexaoForcada { get; init; }

    /// <summary>CPFs válidos que a fábrica configura como ministro e diretor (o resto é servidor, nível 1).</summary>
    public const string CpfDoMinistro = "52998224725";

    public const string CpfDoDiretor = "11144477735";

    public const string ChaveJwt = "chave-de-teste-com-mais-de-trinta-e-dois-bytes-123456";

    /// <summary>Liga o cadastro sem rosto (modo de desenvolvimento). Desligado, o cadastro fica pendente.</summary>
    public bool CadastroSemRosto { get; init; } = true;

    /// <summary>Multiplica os limites de requisição. Alto por padrão, para os testes não esbarrarem no rate limit.</summary>
    public int FatorDeLimite { get; init; } = 1000;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Dados", ConexaoForcada ?? _conexao);
        builder.UseSetting("RateLimit:Fator", FatorDeLimite.ToString(System.Globalization.CultureInfo.InvariantCulture));
        builder.UseSetting("Jwt:Chave", ChaveJwt);
        builder.UseSetting("Biometria:ChaveCifra", Convert.ToBase64String(new byte[32].Select((_, i) => (byte)(i + 1)).ToArray()));
        builder.UseSetting("Niveis:Ministro", CpfDoMinistro);
        builder.UseSetting("Niveis:Diretores:0", CpfDoDiretor);
        builder.UseSetting("Auth:CadastroSemRosto", CadastroSemRosto ? "true" : "false");
        builder.ConfigureServices(s =>
        {
            s.AddTransient<IStartupFilter, FiltroDeRotaQueFalha>();
            s.AddSingleton<IClienteFacial>(Facial);
        });
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
