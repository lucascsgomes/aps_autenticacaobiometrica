using Banco.Dominio.Usuarios;

namespace Banco.Tests;

public class CpfTests
{
    [Theory]
    [InlineData("52998224725")]
    [InlineData("11144477735")]
    public void Aceita_cpf_com_digitos_verificadores_corretos(string cpf) => Assert.True(Cpf.EhValido(cpf));

    [Theory]
    [InlineData("52998224724")] // dígito verificador errado
    [InlineData("11111111111")] // todos iguais passam na conta, mas não existem
    [InlineData("529.982.247-25")] // pontuação é rejeitada, como no Pix
    [InlineData("5299822472")]
    [InlineData("")]
    [InlineData(null)]
    public void Recusa_cpf_invalido(string? cpf) => Assert.False(Cpf.EhValido(cpf));
}

public class UsuarioBloqueioTests
{
    private static readonly DateTimeOffset Agora = new(2026, 3, 10, 14, 0, 0, TimeSpan.Zero);

    private static Usuario Novo() => Usuario.Cadastrar("Ana Souza", "52998224725", "ana@exemplo.com", null, "hash", Agora);

    [Fact]
    public void Quatro_falhas_nao_bloqueiam()
    {
        var u = Novo();
        for (var i = 0; i < 4; i++)
        {
            u.RegistrarFalhaDeCredencial(Agora);
        }

        Assert.False(u.EstaBloqueado(Agora));
    }

    [Theory]
    [InlineData(5, 1)]
    [InlineData(6, 2)]
    [InlineData(7, 4)]
    [InlineData(8, 8)]
    [InlineData(9, 15)] // 16 minutos viram o teto de 15
    [InlineData(20, 15)]
    public void A_partir_da_quinta_falha_o_tempo_dobra_ate_o_teto_de_15_minutos(int falhas, int minutosEsperados)
    {
        var u = Novo();
        for (var i = 0; i < falhas; i++)
        {
            u.RegistrarFalhaDeCredencial(Agora);
        }

        Assert.Equal(Agora.AddMinutes(minutosEsperados), u.BloqueadoAte);
        Assert.True(u.EstaBloqueado(Agora));
        Assert.False(u.EstaBloqueado(Agora.AddMinutes(minutosEsperados)));
    }

    [Fact]
    public void Credencial_correta_zera_falhas_e_bloqueio()
    {
        var u = Novo();
        for (var i = 0; i < 5; i++)
        {
            u.RegistrarFalhaDeCredencial(Agora);
        }

        u.RegistrarCredencialCorreta(Agora);

        Assert.Equal(0, u.FalhasConsecutivas);
        Assert.Null(u.BloqueadoAte);
    }

    [Fact]
    public void Cadastro_so_conclui_uma_vez()
    {
        var u = Novo();
        u.ConcluirCadastro(Agora);

        Assert.Equal(StatusUsuario.Ativo, u.Status);
        Assert.Equal(Agora, u.CadastroConcluidoEm);
        Assert.Throws<InvalidOperationException>(() => u.ConcluirCadastro(Agora));
    }
}

public class ReiniciarCadastroTests
{
    private static readonly DateTimeOffset Agora = new(2026, 3, 10, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Troca_os_dados_e_zera_o_bloqueio_mas_mantem_usuario_e_cpf()
    {
        var u = Usuario.Cadastrar("Ana Souza", "52998224725", "ana@exemplo.com", null, "hash", Agora);
        for (var i = 0; i < 5; i++)
        {
            u.RegistrarFalhaDeCredencial(Agora);
        }

        u.ReiniciarCadastro("Ana Beatriz", "Nova@Exemplo.com", "+5511987654321", "hash-novo", "v2", Agora.AddHours(1));

        Assert.Equal("Ana Beatriz", u.Nome);
        Assert.Equal("nova@exemplo.com", u.Email);
        Assert.Equal("hash-novo", u.SenhaHash);
        Assert.Equal("v2", u.VersaoTermoBiometria);
        Assert.Equal("52998224725", u.Cpf);
        Assert.Equal(0, u.FalhasConsecutivas);
        Assert.False(u.EstaBloqueado(Agora));
    }

    [Fact]
    public void So_vale_para_cadastro_pendente_e_dados_validos()
    {
        var u = Usuario.Cadastrar("Ana Souza", "52998224725", "ana@exemplo.com", null, "hash", Agora);
        Assert.Throws<ArgumentException>(() => u.ReiniciarCadastro("A", "ana@exemplo.com", null, "hash", "v1", Agora));

        u.ConcluirCadastro(Agora);
        Assert.Throws<InvalidOperationException>(() => u.ReiniciarCadastro("Ana Souza", "ana@exemplo.com", null, "hash", "v1", Agora));
    }
}

public class RefreshTokenTests
{
    private static readonly DateTimeOffset Agora = new(2026, 3, 10, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Exige_hash_de_32_bytes()
    {
        Assert.Throws<ArgumentException>(() => RefreshToken.Emitir(Guid.NewGuid(), Guid.NewGuid(), new byte[31], Agora, TimeSpan.FromDays(7)));
    }

    [Fact]
    public void Expira_e_registra_o_uso()
    {
        var t = RefreshToken.Emitir(Guid.NewGuid(), Guid.NewGuid(), new byte[32], Agora, TimeSpan.FromDays(7));
        Assert.False(t.Expirou(Agora.AddDays(7).AddSeconds(-1)));
        Assert.True(t.Expirou(Agora.AddDays(7)));
        Assert.False(t.JaFoiUsadoOuRevogado);

        t.MarcarComoUsado(Guid.NewGuid(), Agora);

        Assert.True(t.JaFoiUsadoOuRevogado);
    }
}
