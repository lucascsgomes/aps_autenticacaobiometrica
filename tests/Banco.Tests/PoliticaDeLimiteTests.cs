using Banco.Dominio.Contas;
using Banco.Dominio.Transferencias;

namespace Banco.Tests;

public class PoliticaDeLimiteTests
{
    private static readonly DateTimeOffset Criacao = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    private static Conta NovaConta() => Conta.AbrirParaCliente(Guid.NewGuid(), "123456", Criacao);

    /// <summary>Hora de Brasília (UTC-3) em 10/03/2026.</summary>
    private static DateTimeOffset Em(int dia, int hora, int minuto = 0) => new DateTimeOffset(2026, 3, dia, hora, minuto, 0, TimeSpan.FromHours(-3)).ToUniversalTime();

    [Fact]
    public void De_dia_so_o_limite_diurno_vale_e_a_janela_e_o_dia_civil()
    {
        var j = PoliticaDeLimite.Calcular(NovaConta(), Em(10, 12));

        Assert.Null(j.Noite);
        Assert.Equal(Em(10, 0), j.DiaInicio);
        Assert.Equal(Em(11, 0), j.DiaFim);
    }

    [Fact]
    public void A_noite_depois_das_20h_a_janela_vai_ate_as_6h_do_dia_seguinte()
    {
        var j = PoliticaDeLimite.Calcular(NovaConta(), Em(10, 23));

        Assert.Equal((Em(10, 20), Em(11, 6)), j.Noite);
        Assert.Equal(Em(10, 0), j.DiaInicio);
    }

    [Fact]
    public void De_madrugada_a_janela_noturna_comecou_na_noite_anterior()
    {
        var j = PoliticaDeLimite.Calcular(NovaConta(), Em(11, 3));

        Assert.Equal((Em(10, 20), Em(11, 6)), j.Noite);
        Assert.Equal(Em(11, 0), j.DiaInicio);
    }

    [Fact]
    public void Os_limites_das_6h_e_das_20h_ja_caem_do_lado_certo()
    {
        Assert.Null(PoliticaDeLimite.Calcular(NovaConta(), Em(10, 6)).Noite);
        Assert.NotNull(PoliticaDeLimite.Calcular(NovaConta(), Em(10, 20)).Noite);
        Assert.NotNull(PoliticaDeLimite.Calcular(NovaConta(), Em(10, 5, 59)).Noite);
    }

    [Fact]
    public void Com_inicio_as_22h_as_21h_ainda_sao_dia()
    {
        var conta = NovaConta();
        conta.DefinirLimites(500_000, 100_000, new TimeOnly(22, 0));

        Assert.Null(PoliticaDeLimite.Calcular(conta, Em(10, 21)).Noite);
        Assert.NotNull(PoliticaDeLimite.Calcular(conta, Em(10, 22)).Noite);
    }

    [Fact]
    public void Recusa_pelo_limite_certo_e_aceita_o_valor_que_cabe_exatamente()
    {
        var conta = NovaConta(); // diurno 5.000,00 e noturno 1.000,00
        var noite = PoliticaDeLimite.Calcular(conta, Em(10, 23));
        var dia = PoliticaDeLimite.Calcular(conta, Em(10, 12));

        Assert.Null(PoliticaDeLimite.MotivoDaRecusa(conta, noite, 0, 90_000, 10_000));
        Assert.Equal(MotivoFalha.LimiteNoturno, PoliticaDeLimite.MotivoDaRecusa(conta, noite, 0, 90_000, 10_001));
        Assert.Null(PoliticaDeLimite.MotivoDaRecusa(conta, dia, 400_000, 0, 100_000));
        Assert.Equal(MotivoFalha.LimiteDiurno, PoliticaDeLimite.MotivoDaRecusa(conta, dia, 400_000, 0, 100_001));
        Assert.Equal(MotivoFalha.LimiteDiurno, PoliticaDeLimite.MotivoDaRecusa(conta, noite, 495_000, 0, 5_001));
    }

    [Fact]
    public void Reduzir_vale_na_hora_e_aumentar_ou_encurtar_a_noite_exige_confirmacao()
    {
        var conta = NovaConta();

        Assert.False(conta.ExigeConfirmacao(300_000, 50_000, new TimeOnly(20, 0)));
        Assert.True(conta.ExigeConfirmacao(500_001, 100_000, new TimeOnly(20, 0)));
        Assert.True(conta.ExigeConfirmacao(500_000, 100_001, new TimeOnly(20, 0)));
        Assert.True(conta.ExigeConfirmacao(500_000, 100_000, new TimeOnly(22, 0)));
    }

    [Fact]
    public void Noturno_acima_do_diurno_e_recusado()
    {
        Assert.Throws<ArgumentException>(() => NovaConta().DefinirLimites(100_000, 100_001, new TimeOnly(20, 0)));
    }
}
