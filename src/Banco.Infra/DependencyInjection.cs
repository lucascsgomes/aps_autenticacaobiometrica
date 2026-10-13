using Banco.Infra.Autenticacao;
using Banco.Infra.Cadastro;
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
        servicos.AddScoped<ServicoDeLimites>();
        servicos.AddScoped<ServicoDeIdempotencia>();

        return servicos;
    }
}
