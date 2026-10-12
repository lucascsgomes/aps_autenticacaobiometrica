using Banco.Infra.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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

        return servicos;
    }
}
