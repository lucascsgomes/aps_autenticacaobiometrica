using System.Net;
using System.Net.Sockets;
using Banco.Infra.Persistencia;
using Microsoft.EntityFrameworkCore;
using MysticMind.PostgresEmbed;
using Npgsql;

namespace Banco.Tests.Integracao;

/// <summary>
/// PostgreSQL de verdade, embutido no processo de teste (sem Docker). Sobe uma vez por execução
/// e cada teste pede um banco novo, já com as migrations aplicadas.
/// </summary>
internal static class ServidorPostgres
{
    private static readonly Lazy<(PgServer Servidor, int Porta)> Inicio = new(Iniciar);
    private static int _contador;

    public static async Task<string> CriarBancoAsync()
    {
        var porta = Inicio.Value.Porta;
        var nome = $"teste_{Interlocked.Increment(ref _contador)}_{Guid.NewGuid():N}";

        await using (var admin = new NpgsqlConnection(Conexao(porta, "postgres")))
        {
            await admin.OpenAsync();
            await using var criar = new NpgsqlCommand($"CREATE DATABASE {nome} STRATEGY = WAL_LOG", admin);
            await criar.ExecuteNonQueryAsync();
        }

        var conexao = Conexao(porta, nome);
        var opcoes = new DbContextOptionsBuilder<BancoDbContext>()
            .UseNpgsql(conexao).UseSnakeCaseNamingConvention().Options;
        await using var banco = new BancoDbContext(opcoes);
        await banco.Database.MigrateAsync();
        return conexao;
    }

    public static BancoDbContext Contexto(string conexao) =>
        new(new DbContextOptionsBuilder<BancoDbContext>().UseNpgsql(conexao).UseSnakeCaseNamingConvention().Options);

    private static string Conexao(int porta, string banco) =>
        $"Host=localhost;Port={porta};Database={banco};Username=postgres;Password=postgres;Pooling=false;Command Timeout=120";

    private static (PgServer, int) Iniciar()
    {
        var porta = PortaLivre();
        var servidor = new PgServer("17.5.0", port: porta, clearInstanceDirOnStop: true, pgServerParams: new Dictionary<string, string>
        {
            // Só em teste: durabilidade não importa e o fsync deixa tudo lento no Windows.
            ["fsync"] = "off",
            ["synchronous_commit"] = "off",
            ["full_page_writes"] = "off",
            // O log do servidor vai para um pipe que ninguém lê: ao encher, a sessão trava ao registrar um erro de constraint.
            ["log_min_messages"] = "panic",
            ["log_min_error_statement"] = "panic",
        });
        servidor.Start();
        AguardarAceitarConexoes(porta);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => servidor.Stop();
        return (servidor, porta);
    }

    // Start() devolve antes do servidor responder consultas no Windows; sem a espera os primeiros testes estouram o tempo.
    private static void AguardarAceitarConexoes(int porta)
    {
        for (var tentativa = 0; ; tentativa++)
        {
            try
            {
                using var c = new NpgsqlConnection(Conexao(porta, "postgres"));
                c.Open();
                using var q = new NpgsqlCommand("SELECT 1", c);
                q.ExecuteScalar();
                return;
            }
            catch (NpgsqlException) when (tentativa < 60)
            {
                Thread.Sleep(1000);
            }
        }
    }

    private static int PortaLivre()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var porta = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return porta;
    }
}
