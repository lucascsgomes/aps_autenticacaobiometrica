using Banco.Dominio.Contas;
using Banco.Dominio.Usuarios;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Banco.Tests.Integracao;

/// <summary>Prova, em PostgreSQL real, que as regras vivem no banco e não só na aplicação.</summary>
public class SchemaTests(SchemaTests.BancoCompartilhado compartilhado) : IClassFixture<SchemaTests.BancoCompartilhado>
{
    /// <summary>Um banco para a classe toda: criar um por teste trava o servidor embutido no Windows.</summary>
    public sealed class BancoCompartilhado : IAsyncLifetime
    {
        public string Conexao { get; private set; } = "";
        public async Task InitializeAsync() => Conexao = await ServidorPostgres.CriarBancoAsync();
        public Task DisposeAsync() => Task.CompletedTask;
    }

    private static int _sequencia;

    private const string UniqueViolation = "23505";
    private const string CheckViolation = "23514";

    private static string NovoNumero() => Interlocked.Increment(ref _sequencia).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);

    private static Usuario NovoUsuario(string? cpf = null, string? email = null)
    {
        var n = Interlocked.Increment(ref _sequencia);
        return Usuario.Cadastrar("Ana Souza", cpf ?? $"{n:D11}", email ?? $"ana{n}@exemplo.com", "+5511987654321", "hash", DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Usuario_e_conta_fazem_ida_e_volta_com_enums_em_texto()
    {
        var conexao = compartilhado.Conexao;
        var usuario = NovoUsuario();
        await using (var escrita = ServidorPostgres.Contexto(conexao))
        {
            escrita.Usuarios.Add(usuario);
            escrita.Contas.Add(Conta.AbrirParaCliente(usuario.Id, NovoNumero(), DateTimeOffset.UtcNow));
            await escrita.SaveChangesAsync();
        }

        await using var leitura = ServidorPostgres.Contexto(conexao);
        var lido = await leitura.Usuarios.SingleAsync(u => u.Id == usuario.Id);
        var conta = await leitura.Contas.SingleAsync(c => c.UsuarioId == usuario.Id);
        var bruto = await leitura.Database.SqlQuery<string>($"SELECT status AS \"Value\" FROM usuarios WHERE id = {usuario.Id}").SingleAsync();

        Assert.Equal(StatusUsuario.CadastroPendente, lido.Status);
        Assert.Equal("CADASTRO_PENDENTE", bruto);
        Assert.Equal(TipoConta.Cliente, conta.Tipo);
        Assert.Equal(0, conta.Saldo.Centavos);
    }

    [Fact]
    public async Task Cpf_e_email_repetidos_sao_recusados()
    {
        var conexao = compartilhado.Conexao;
        await using var banco = ServidorPostgres.Contexto(conexao);
        var primeiro = NovoUsuario();
        banco.Usuarios.Add(primeiro);
        await banco.SaveChangesAsync();

        banco.Usuarios.Add(NovoUsuario(cpf: primeiro.Cpf));
        var mesmoCpf = await Assert.ThrowsAsync<DbUpdateException>(() => banco.SaveChangesAsync());

        Assert.Equal(UniqueViolation, ((PostgresException)mesmoCpf.InnerException!).SqlState);
    }

    [Fact]
    public async Task Usuario_nao_pode_ficar_ativo_sem_data_de_conclusao_do_cadastro()
    {
        var conexao = compartilhado.Conexao;
        await using var banco = ServidorPostgres.Contexto(conexao);
        var usuario = NovoUsuario();
        banco.Usuarios.Add(usuario);
        await banco.SaveChangesAsync();

        var erro = await Assert.ThrowsAsync<PostgresException>(
            () => banco.Database.ExecuteSqlAsync($"UPDATE usuarios SET status = 'ATIVO' WHERE id = {usuario.Id}"));

        Assert.Equal(CheckViolation, erro.SqlState);
    }

    [Fact]
    public async Task Conta_de_cliente_nao_fica_com_saldo_negativo_mas_a_de_sistema_pode()
    {
        var conexao = await ServidorPostgres.CriarBancoAsync();
        var usuario = NovoUsuario();
        await using var banco = ServidorPostgres.Contexto(conexao);
        banco.Usuarios.Add(usuario);
        banco.Contas.Add(Conta.AbrirParaCliente(usuario.Id, NovoNumero(), DateTimeOffset.UtcNow));
        banco.Contas.Add(Conta.AbrirDoSistema("000001", DateTimeOffset.UtcNow));
        await banco.SaveChangesAsync();

        var cliente = await Assert.ThrowsAsync<PostgresException>(
            () => banco.Database.ExecuteSqlRawAsync("UPDATE contas SET saldo_centavos = -1 WHERE tipo = 'CLIENTE'"));
        var sistema = await banco.Database.ExecuteSqlRawAsync("UPDATE contas SET saldo_centavos = -500000 WHERE tipo = 'SISTEMA'");

        Assert.Equal(CheckViolation, cliente.SqlState);
        Assert.Equal(1, sistema);
    }

    [Fact]
    public async Task Um_usuario_so_pode_ter_uma_conta_de_cliente()
    {
        var conexao = compartilhado.Conexao;
        var usuario = NovoUsuario();
        await using var banco = ServidorPostgres.Contexto(conexao);
        banco.Usuarios.Add(usuario);
        banco.Contas.Add(Conta.AbrirParaCliente(usuario.Id, NovoNumero(), DateTimeOffset.UtcNow));
        await banco.SaveChangesAsync();

        banco.Contas.Add(Conta.AbrirParaCliente(usuario.Id, NovoNumero(), DateTimeOffset.UtcNow));
        var erro = await Assert.ThrowsAsync<DbUpdateException>(() => banco.SaveChangesAsync());

        Assert.Equal(UniqueViolation, ((PostgresException)erro.InnerException!).SqlState);
    }

    [Fact]
    public async Task Existe_no_maximo_uma_conta_de_sistema()
    {
        var conexao = await ServidorPostgres.CriarBancoAsync();
        await using var banco = ServidorPostgres.Contexto(conexao);
        banco.Contas.Add(Conta.AbrirDoSistema("000001", DateTimeOffset.UtcNow));
        await banco.SaveChangesAsync();

        banco.Contas.Add(Conta.AbrirDoSistema("000002", DateTimeOffset.UtcNow));
        var erro = await Assert.ThrowsAsync<DbUpdateException>(() => banco.SaveChangesAsync());

        Assert.Equal(UniqueViolation, ((PostgresException)erro.InnerException!).SqlState);
    }

    [Fact]
    public async Task Conta_de_cliente_sem_dono_e_recusada_pelo_banco()
    {
        var conexao = compartilhado.Conexao;
        await using var banco = ServidorPostgres.Contexto(conexao);

        var erro = await Assert.ThrowsAsync<PostgresException>(() => banco.Database.ExecuteSqlRawAsync(
            "INSERT INTO contas (id, tipo, agencia, numero, saldo_centavos, limite_diurno_centavos, limite_noturno_centavos, " +
            "inicio_noturno, fim_noturno, status, criado_em) " +
            "VALUES (gen_random_uuid(), 'CLIENTE', '0001', '1', 0, 1, 1, '20:00', '06:00', 'ATIVA', now())"));

        Assert.Equal(CheckViolation, erro.SqlState);
    }
}
