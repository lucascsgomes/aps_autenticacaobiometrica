using Banco.Web.Servicos;

namespace Banco.Tests;

public class FormatoTests
{
    [Theory]
    [InlineData(0, "R$ 0,00")]
    [InlineData(5, "R$ 0,05")]
    [InlineData(123456, "R$ 1.234,56")]
    [InlineData(500000, "R$ 5.000,00")]
    [InlineData(100000000, "R$ 1.000.000,00")]
    [InlineData(-2550, "-R$ 25,50")]
    public void Reais_formata_em_portugues_sem_depender_da_cultura(long centavos, string esperado) =>
        Assert.Equal(esperado, Formato.Reais(centavos));

    [Theory]
    [InlineData("111.444.777-35", "11144477735")]
    [InlineData(" 111 444 777 35 ", "11144477735")]
    [InlineData(null, "")]
    public void SoDigitos_descarta_pontuacao(string? entrada, string esperado) =>
        Assert.Equal(esperado, Formato.SoDigitos(entrada));

    [Theory]
    [InlineData("25", 2500)]
    [InlineData("25,5", 2550)]
    [InlineData("25,50", 2550)]
    [InlineData("25.50", 2550)]
    [InlineData("0,01", 1)]
    [InlineData("R$ 1.234,56", 123456)]
    [InlineData("1.234", 123400)]
    [InlineData("1.234.567", 123456700)]
    public void TentarCentavos_aceita_as_formas_comuns_de_digitar(string entrada, long esperado)
    {
        Assert.True(Formato.TentarCentavos(entrada, out var centavos));
        Assert.Equal(esperado, centavos);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("0")]
    [InlineData("0,00")]
    [InlineData("-5")]
    [InlineData("abc")]
    [InlineData("1,234")]
    [InlineData("1,2,3")]
    [InlineData("12345678901")]
    public void TentarCentavos_recusa_o_que_nao_e_um_valor_positivo_com_duas_casas(string? entrada) =>
        Assert.False(Formato.TentarCentavos(entrada, out _));
}
