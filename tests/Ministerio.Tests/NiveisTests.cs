using Ministerio.Dominio.Acesso;
using Ministerio.Dominio.Biometria;
using Ministerio.Dominio.Usuarios;
using Ministerio.Infra.Acesso;
using Ministerio.Infra.Autenticacao;
using Microsoft.Extensions.Configuration;

namespace Ministerio.Tests;

public class NiveisTests
{
    private static ServicoDeNiveis Servico(string? ministro, params string[] diretores)
    {
        var dados = new Dictionary<string, string?>();
        if (ministro is not null)
        {
            dados["Niveis:Ministro"] = ministro;
        }

        for (var i = 0; i < diretores.Length; i++)
        {
            dados[$"Niveis:Diretores:{i}"] = diretores[i];
        }

        return new ServicoDeNiveis(new ConfigurationBuilder().AddInMemoryCollection(dados).Build());
    }

    [Fact]
    public void Cada_cpf_cai_no_nivel_da_lista_em_que_esta_e_o_resto_e_servidor()
    {
        var servico = Servico("11111111111", "22222222222", "33333333333");

        Assert.Equal(Niveis.Ministro, servico.De("11111111111"));
        Assert.Equal(Niveis.Diretor, servico.De("22222222222"));
        Assert.Equal(Niveis.Diretor, servico.De("33333333333"));
        Assert.Equal(Niveis.Servidor, servico.De("44444444444"));
    }

    [Fact]
    public void Sem_nada_configurado_ninguem_e_ministro_nem_diretor()
    {
        var servico = Servico(null);

        Assert.Equal(Niveis.Servidor, servico.De("11111111111"));
        Assert.Equal(Niveis.Servidor, servico.De(string.Empty));
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
        Assert.True(limiares.Do(FinalidadeDoDesafio.AcessoNivel3) > limiares.Do(FinalidadeDoDesafio.AcessoNivel2));
        Assert.True(limiares.Do(FinalidadeDoDesafio.AcessoNivel2) > limiares.Login);
    }

    [Fact]
    public void O_hash_do_desafio_muda_com_a_finalidade_o_dono_e_o_nonce()
    {
        var dono = Guid.NewGuid();
        var nonce = new byte[32];

        var base1 = DesafioBiometrico.HashDaAcao(dono, FinalidadeDoDesafio.AcessoNivel2, "{}", nonce);

        Assert.Equal(base1, DesafioBiometrico.HashDaAcao(dono, FinalidadeDoDesafio.AcessoNivel2, "{}", nonce));
        Assert.NotEqual(base1, DesafioBiometrico.HashDaAcao(dono, FinalidadeDoDesafio.AcessoNivel3, "{}", nonce));
        Assert.NotEqual(base1, DesafioBiometrico.HashDaAcao(Guid.NewGuid(), FinalidadeDoDesafio.AcessoNivel2, "{}", nonce));
        Assert.NotEqual(base1, DesafioBiometrico.HashDaAcao(dono, FinalidadeDoDesafio.AcessoNivel2, "{}", [1, .. new byte[31]]));
    }

    [Fact]
    public void O_cpf_mascarado_so_mostra_os_tres_digitos_do_meio()
    {
        Assert.Equal("***.444.777-**", Cpf.Mascarar("11144477735"));
    }
}
