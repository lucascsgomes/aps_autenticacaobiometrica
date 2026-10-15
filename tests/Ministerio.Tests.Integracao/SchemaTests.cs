using Ministerio.Dominio.Usuarios;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Ministerio.Tests.Integracao;

/// <summary>Prova, em PostgreSQL real, que as regras vivem na base e não só na aplicação.</summary>
public class SchemaTests(SchemaTests.BaseCompartilhada compartilhado) : IClassFixture<SchemaTests.BaseCompartilhada>
{
    /// <summary>Uma base para a classe toda: criar uma por teste trava o servidor embutido no Windows.</summary>
    public sealed class BaseCompartilhada : IAsyncLifetime
    {
        public string Conexao { get; private set; } = "";
        public async Task InitializeAsync() => Conexao = await ServidorPostgres.CriarBaseAsync();
        public Task DisposeAsync() => Task.CompletedTask;
    }

    private static int _sequencia;

    private const string UniqueViolation = "23505";
    private const string CheckViolation = "23514";

    private static Usuario NovoUsuario(string? cpf = null, string? email = null)
    {
        var n = Interlocked.Increment(ref _sequencia);
        return Usuario.Cadastrar("Ana Souza", cpf ?? $"{n:D11}", email ?? $"ana{n}@exemplo.com", "+5511987654321", "hash", DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Usuario_faz_ida_e_volta_com_o_status_em_texto()
    {
        var conexao = compartilhado.Conexao;
        var usuario = NovoUsuario();
        await using (var escrita = ServidorPostgres.Contexto(conexao))
        {
            escrita.Usuarios.Add(usuario);
            await escrita.SaveChangesAsync();
        }

        await using var leitura = ServidorPostgres.Contexto(conexao);
        var lido = await leitura.Usuarios.SingleAsync(u => u.Id == usuario.Id);
        var bruto = await leitura.Database.SqlQuery<string>($"SELECT status AS \"Value\" FROM usuarios WHERE id = {usuario.Id}").SingleAsync();

        Assert.Equal(StatusUsuario.CadastroPendente, lido.Status);
        Assert.Equal("CADASTRO_PENDENTE", bruto);
    }

    [Fact]
    public async Task Cpf_e_email_repetidos_sao_recusados()
    {
        var conexao = compartilhado.Conexao;
        await using var db = ServidorPostgres.Contexto(conexao);
        var primeiro = NovoUsuario();
        db.Usuarios.Add(primeiro);
        await db.SaveChangesAsync();

        db.Usuarios.Add(NovoUsuario(cpf: primeiro.Cpf));
        var mesmoCpf = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

        Assert.Equal(UniqueViolation, ((PostgresException)mesmoCpf.InnerException!).SqlState);
    }

    [Fact]
    public async Task Usuario_nao_pode_ficar_ativo_sem_data_de_conclusao_do_cadastro()
    {
        var conexao = compartilhado.Conexao;
        await using var db = ServidorPostgres.Contexto(conexao);
        var usuario = NovoUsuario();
        db.Usuarios.Add(usuario);
        await db.SaveChangesAsync();

        var erro = await Assert.ThrowsAsync<PostgresException>(
            () => db.Database.ExecuteSqlAsync($"UPDATE usuarios SET status = 'ATIVO' WHERE id = {usuario.Id}"));

        Assert.Equal(CheckViolation, erro.SqlState);
    }

    [Fact]
    public async Task A_base_ambiental_nasce_com_os_vinte_registros_ficticios()
    {
        await using var db = ServidorPostgres.Contexto(compartilhado.Conexao);

        var total = await db.Propriedades.CountAsync();
        var semNivel3 = await db.Propriedades.CountAsync(p => p.MultaCentavos <= 0 || p.Proprietario == "");

        Assert.Equal(20, total);
        Assert.Equal(0, semNivel3);
    }

    [Theory]
    [InlineData("go")]
    [InlineData("GOIAS")]
    public async Task Propriedade_com_uf_invalida_e_recusada_pela_base(string uf)
    {
        await using var db = ServidorPostgres.Contexto(compartilhado.Conexao);

        var erro = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO propriedades_rurais (id, municipio, uf, agrotoxico, ano, propriedade, quantidade_litros, area_hectares, proprietario, documento, multa_centavos, situacao)
            VALUES (gen_random_uuid(), 'X', {uf}, 'Y', 2024, 'Z', 1, 1, 'W', 'D', 1, 'S')
            """));

        Assert.True(erro.SqlState is CheckViolation or "22001"); // UF minúscula viola o CHECK; UF comprida não cabe em char(2)
    }

    [Fact]
    public async Task Registro_de_acesso_recusado_sem_motivo_e_recusado_pela_base()
    {
        await using var db = ServidorPostgres.Contexto(compartilhado.Conexao);

        var erro = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
            "INSERT INTO registros_de_acesso (ocorrido_em, evento, resultado, motivo) VALUES (now(), 'LOGIN_SENHA', 'RECUSADO', NULL)"));

        Assert.Equal(CheckViolation, erro.SqlState);
    }

    [Fact]
    public async Task Registro_de_acesso_aceito_com_motivo_e_recusado_pela_base()
    {
        await using var db = ServidorPostgres.Contexto(compartilhado.Conexao);

        var erro = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
            "INSERT INTO registros_de_acesso (ocorrido_em, evento, resultado, motivo) VALUES (now(), 'LOGIN_SENHA', 'ACEITO', 'FACE_NOT_VERIFIED')"));

        Assert.Equal(CheckViolation, erro.SqlState);
    }

    [Fact]
    public async Task Desafio_de_finalidade_desconhecida_e_recusado_pela_base()
    {
        var conexao = compartilhado.Conexao;
        var usuario = NovoUsuario();
        await using var db = ServidorPostgres.Contexto(conexao);
        db.Usuarios.Add(usuario);
        await db.SaveChangesAsync();

        var erro = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO desafios_biometricos (id, usuario_id, finalidade, nonce, hash_operacao, limiar_aplicado, criado_em, expira_em, devolvido_vezes)
            VALUES (gen_random_uuid(), {usuario.Id}, 'TRANSFERENCIA', decode(repeat('ab', 32), 'hex'), decode(repeat('cd', 32), 'hex'), 0.45, now(), now() + interval '90 seconds', 0)
            """));

        Assert.Equal(CheckViolation, erro.SqlState);
    }
}
