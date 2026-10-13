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
}
