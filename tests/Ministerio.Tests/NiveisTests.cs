using Ministerio.Dominio.Acesso;
using Ministerio.Dominio.Biometria;
using Ministerio.Dominio.Usuarios;
using Ministerio.Infra.Acesso;
using Ministerio.Infra.Autenticacao;
using Microsoft.Extensions.Configuration;

namespace Ministerio.Tests;

public class NiveisTests
{
    private static ServicoDeAdmins Servico(params string[] admins)
    {
        var dados = admins.Select((cpf, i) => ($"Admin:Cpfs:{i}", (string?)cpf)).ToDictionary(p => p.Item1, p => p.Item2);
        return new ServicoDeAdmins(new ConfigurationBuilder().AddInMemoryCollection(dados).Build());
    }

    private static Usuario NovoUsuario() =>
        Usuario.Cadastrar("Ana Souza", "52998224725", "ana@exemplo.com", null, "hash", DateTimeOffset.UtcNow);

    [Fact]
    public void Admin_da_configuracao_e_fixo_e_sempre_admin()
    {
        var servico = Servico("11111111111", "22222222222");

        Assert.True(servico.Fixo("11111111111"));
        Assert.True(servico.EhAdmin("22222222222", adminNoBanco: false));
        Assert.False(servico.Fixo("33333333333"));
        Assert.False(servico.EhAdmin("33333333333", adminNoBanco: false));
    }

    [Fact]
    public void Admin_do_banco_vale_mesmo_fora_da_configuracao_mas_nao_e_fixo()
    {
        var servico = Servico("11111111111");

        Assert.True(servico.EhAdmin("33333333333", adminNoBanco: true));
        Assert.False(servico.Fixo("33333333333"));
    }

    [Fact]
    public void Sem_nada_configurado_ninguem_e_admin_pela_configuracao_e_cpf_vazio_nao_conta()
    {
        var servico = Servico(string.Empty, "  ");

        Assert.False(servico.Fixo(string.Empty));
        Assert.False(servico.EhAdmin("11111111111", adminNoBanco: false));
    }

    [Fact]
    public void Usuario_nasce_servidor_sem_admin_e_o_acesso_muda_so_o_que_veio()
    {
        var usuario = NovoUsuario();
        Assert.Equal((Niveis.Servidor, false), (usuario.Nivel, usuario.Admin));

        usuario.DefinirAcesso(Niveis.Diretor, null, DateTimeOffset.UtcNow);
        Assert.Equal((Niveis.Diretor, false), (usuario.Nivel, usuario.Admin));

        usuario.DefinirAcesso(null, true, DateTimeOffset.UtcNow);
        Assert.Equal((Niveis.Diretor, true), (usuario.Nivel, usuario.Admin));

        usuario.DefinirAcesso(Niveis.Ministro, false, DateTimeOffset.UtcNow);
        Assert.Equal((Niveis.Ministro, false), (usuario.Nivel, usuario.Admin));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(-1)]
    public void Nivel_fora_de_1_a_3_e_recusado_e_nao_muda_nada(int nivel)
    {
        var usuario = NovoUsuario();

        Assert.Throws<ArgumentOutOfRangeException>(() => usuario.DefinirAcesso(nivel, true, DateTimeOffset.UtcNow));

        Assert.Equal((Niveis.Servidor, false), (usuario.Nivel, usuario.Admin));
    }

    [Theory]
    [InlineData(1, "Servidor")]
    [InlineData(2, "Diretor de divisão")]
    [InlineData(3, "Ministro do Meio Ambiente")]
    public void Cada_nivel_tem_o_seu_cargo(int nivel, string cargo) => Assert.Equal(cargo, Niveis.Cargo(nivel));

    [Fact]
    public void So_os_niveis_restritos_tem_desafio_de_rosto()
    {
        Assert.Null(Niveis.FinalidadeDoAcesso(Niveis.Servidor));
        Assert.Equal(FinalidadeDoDesafio.AcessoNivel2, Niveis.FinalidadeDoAcesso(Niveis.Diretor));
        Assert.Equal(FinalidadeDoDesafio.AcessoNivel3, Niveis.FinalidadeDoAcesso(Niveis.Ministro));
    }

    [Fact]
    public void O_limiar_do_nivel_3_e_mais_rigido_que_o_do_nivel_2_e_o_do_login()
    {
        var limiares = new LimiaresFaciais(0.363m, 0.400m, 0.500m, 0.05m);

        Assert.Equal(0.500m, limiares.Do(FinalidadeDoDesafio.AcessoNivel3));
        Assert.Equal(0.400m, limiares.Do(FinalidadeDoDesafio.AcessoNivel2));
        Assert.Equal(0.500m, limiares.Do(FinalidadeDoDesafio.TrocarSenha));
        Assert.Equal(0.500m, limiares.Do(FinalidadeDoDesafio.Recadastro));
        Assert.Equal(0.500m, limiares.Do(FinalidadeDoDesafio.AlterarAcesso));
        Assert.True(limiares.Do(FinalidadeDoDesafio.AcessoNivel2) > limiares.Login);
    }

    [Fact]
    public void O_hash_do_desafio_muda_com_a_finalidade_o_dono_os_parametros_e_o_nonce()
    {
        var dono = Guid.NewGuid();
        var nonce = new byte[32];

        var base1 = DesafioBiometrico.HashDaAcao(dono, FinalidadeDoDesafio.AlterarAcesso, "{\"nivel\":2}", nonce);

        Assert.Equal(base1, DesafioBiometrico.HashDaAcao(dono, FinalidadeDoDesafio.AlterarAcesso, "{\"nivel\":2}", nonce));
        Assert.NotEqual(base1, DesafioBiometrico.HashDaAcao(dono, FinalidadeDoDesafio.AlterarAcesso, "{\"nivel\":3}", nonce));
        Assert.NotEqual(base1, DesafioBiometrico.HashDaAcao(dono, FinalidadeDoDesafio.AcessoNivel2, "{\"nivel\":2}", nonce));
        Assert.NotEqual(base1, DesafioBiometrico.HashDaAcao(Guid.NewGuid(), FinalidadeDoDesafio.AlterarAcesso, "{\"nivel\":2}", nonce));
        Assert.NotEqual(base1, DesafioBiometrico.HashDaAcao(dono, FinalidadeDoDesafio.AlterarAcesso, "{\"nivel\":2}", [1, .. new byte[31]]));
    }

    [Fact]
    public void O_cpf_mascarado_so_mostra_os_tres_digitos_do_meio()
    {
        Assert.Equal("***.444.777-**", Cpf.Mascarar("11144477735"));
    }
}
