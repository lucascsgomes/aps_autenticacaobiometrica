using Ministerio.Infra.Acesso;
using Ministerio.Infra.Autenticacao;
using Ministerio.Infra.Cadastro;
using Ministerio.Infra.Facial;
using Ministerio.Infra.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Ministerio.Infra;

public static class DependencyInjection
{
    public const string NomeConnectionString = "Dados";

    public static IServiceCollection AdicionarInfra(this IServiceCollection servicos, IConfiguration configuracao)
    {
        // Sem connection string o app sobe mesmo assim: /health responde e /ready acusa a base ausente.
        var conexao = configuracao.GetConnectionString(NomeConnectionString);

        servicos.AddDbContext<MinisterioDbContext>(o => o
            .UseNpgsql(string.IsNullOrWhiteSpace(conexao) ? "Host=indefinido" : conexao)
            .UseSnakeCaseNamingConvention());

        servicos.TryAddSingleton(TimeProvider.System);
        servicos.AddSingleton<ServicoDeSenha>();
        servicos.AddScoped<RefreshTokenServico>();
        servicos.AddScoped<ServicoDeAutenticacao>();
        servicos.AddScoped<ServicoDeCadastro>();
        servicos.AddScoped<ServicoDeDesafios>();
        servicos.AddScoped<ServicoDeLoginFacial>();
        servicos.AddScoped<ServicoDeStepUp>();
        servicos.AddScoped<ServicoDeCredenciais>();
        servicos.AddSingleton(new LimiaresFaciais(
            configuracao.GetValue("Biometria:LimiarLogin", 0.363m),
            configuracao.GetValue("Biometria:LimiarNivel2", 0.400m),
            configuracao.GetValue("Biometria:LimiarNivel3", 0.500m),
            configuracao.GetValue("Biometria:Margem", 0.05m)));
        servicos.AddScoped<ServicoDeIdentificacao>();
        servicos.AddSingleton<ServicoDeNiveis>();
        servicos.AddScoped<ServicoDeAuditoria>();

        AdicionarFacial(servicos, configuracao);
        return servicos;
    }

    /// <summary>
    /// Sem <c>Face:BaseUrl</c> o rosto fica indisponível (falha fechada). Com ele, a chave de cifra do template é obrigatória:
    /// melhor não subir do que capturar um rosto que não se pode guardar.
    /// </summary>
    private static void AdicionarFacial(IServiceCollection servicos, IConfiguration configuracao)
    {
        var cifra = new CifraDeTemplate(configuracao["Biometria:ChaveCifra"], configuracao.GetValue<short>("Biometria:ChaveVersao", 1));
        servicos.AddSingleton(cifra);

        var face = configuracao.GetSection(OpcoesFaciais.Secao).Get<OpcoesFaciais>();
        if (string.IsNullOrWhiteSpace(face?.BaseUrl))
        {
            servicos.AddSingleton<IClienteFacial, ClienteFacialNaoConfigurado>();
            return;
        }

        if (!cifra.Configurada || string.IsNullOrWhiteSpace(face.ChaveInterna))
        {
            throw new InvalidOperationException("Face:BaseUrl exige Face:ChaveInterna e Biometria:ChaveCifra (32 bytes em base64).");
        }

        servicos.AddHttpClient<IClienteFacial, ClienteFacialHttp>(c =>
            {
                c.BaseAddress = new Uri(face.BaseUrl.TrimEnd('/') + "/");
                c.DefaultRequestHeaders.Add("X-Internal-Key", face.ChaveInterna);
            })
            .AddStandardResilienceHandler(o =>
            {
                o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(5);
                o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(10);
                o.Retry.MaxRetryAttempts = 1;
                // Pouco tráfego: com o padrão o breaker nunca abriria.
                o.CircuitBreaker.MinimumThroughput = 5;
                o.CircuitBreaker.FailureRatio = 0.5;
                o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
                o.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(15);
            });
    }
}
