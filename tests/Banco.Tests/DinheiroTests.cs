using Banco.Dominio.Contas;

namespace Banco.Tests;

public class DinheiroTests
{
    [Fact]
    public void Soma_de_valores_em_centavos_e_exata()
    {
        var total = Dinheiro.DeReais(0.10m) + Dinheiro.DeReais(0.20m);

        Assert.Equal(30, total.Centavos);
    }

    [Fact]
    public void Subtrair_alem_do_saldo_e_recusado()
    {
        var saldo = Dinheiro.DeReais(10m);

        Assert.Throws<InvalidOperationException>(() => saldo - Dinheiro.DeReais(10.01m));
    }

    [Fact]
    public void Valor_negativo_ou_com_fracao_de_centavo_e_recusado()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Dinheiro.DeCentavos(-1));
        Assert.Throws<ArgumentException>(() => Dinheiro.DeReais(1.005m));
    }
}
