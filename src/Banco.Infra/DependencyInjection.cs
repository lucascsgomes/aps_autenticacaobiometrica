using Banco.Infra.Autenticacao;
using Banco.Infra.Cadastro;
using Banco.Infra.Facial;
using Banco.Infra.Idempotencia;
using Banco.Infra.Persistencia;
using Banco.Infra.Transferencias;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Banco.Infra;

public static class DependencyInjection
{
    public const string NomeConnectionString = "Banco";

    public static IServiceCollection AdicionarInfra(this IServiceCollection servicos, IConfiguration configuracao)
    {
        // Sem connection string o app sobe mesmo assim: /health responde e /ready acusa o banco ausente.
        var conexao = configuracao.GetConnectionString(NomeConnectionString);

        servicos.AddDbContext<BancoDbContext>(o => o
            .UseNpgsql(string.IsNullOrWhiteSpace(conexao) ? "Host=indefinido" : conexao)
            .UseSnakeCaseNamingConvention());

        servicos.TryAddSingleton(TimeProvider.System);
        servicos.AddScoped<ExecutorDeTransferencia>();
        servicos.AddSingleton<ServicoDeSenha>();
        servicos.AddScoped<RefreshTokenServico>();
        servicos.AddScoped<ServicoDeAutenticacao>();
        servicos.AddScoped<ServicoDeCadastro>();
        servicos.AddScoped<ServicoDeTransferencia>();
        servicos.AddScoped<ServicoDeDesafios>();
        servicos.AddScoped<ServicoDeLoginFacial>();
        servicos.AddSingleton(new LimiaresFaciais(configuracao.GetValue("Biometria:LimiarLogin", 0.363m)));
        servicos.AddScoped<ServicoDeLimites>();
        servicos.AddScoped<ServicoDeIdempotencia>();

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
