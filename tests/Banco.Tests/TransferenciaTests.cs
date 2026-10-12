using Banco.Dominio.Contas;
using Banco.Dominio.Transferencias;

namespace Banco.Tests;

public class TransferenciaTests
{
    private static readonly DateTimeOffset Agora = new(2026, 3, 10, 14, 30, 0, TimeSpan.Zero);

    private static Transferencia Nova(long centavos = 1000) =>
        Transferencia.Criar(Guid.NewGuid(), Guid.NewGuid(), Dinheiro.DeCentavos(centavos), "almoço", Agora);

    [Fact]
    public void Nasce_pendente_e_percorre_o_caminho_feliz()
    {
        var t = Nova();
        Assert.Equal(StatusTransferencia.PendenteBiometria, t.Status);

        t.Confirmar(MetodoConfirmacao.Face);
        Assert.Equal(StatusTransferencia.Processando, t.Status);
        Assert.Equal(MetodoConfirmacao.Face, t.MetodoConfirmacao);

        t.Concluir(Agora);
        Assert.Equal(StatusTransferencia.Concluida, t.Status);
        Assert.Equal(Agora, t.ConcluidaEm);
        Assert.Matches("^E[0-9]{8}202603101430[A-Za-z0-9]{11}$", t.E2eId);
    }

    [Fact]
    public void Pendente_pode_falhar_ou_expirar_e_processando_pode_falhar()
    {
        var recusada = Nova();
        recusada.Falhar(MotivoFalha.BiometriaRecusada);
        Assert.Equal(MotivoFalha.BiometriaRecusada, recusada.MotivoFalha);

        var expirada = Nova();
        expirada.Expirar();
        Assert.Equal(StatusTransferencia.Expirada, expirada.Status);

        var semSaldo = Nova();
        semSaldo.Confirmar(MetodoConfirmacao.Senha);
        semSaldo.Falhar(MotivoFalha.SaldoInsuficiente);
        Assert.Equal(StatusTransferencia.Falha, semSaldo.Status);
    }

    [Fact]
    public void Pendente_nao_conclui_sem_passar_por_processando()
    {
        Assert.Throws<InvalidOperationException>(() => Nova().Concluir(Agora));
    }

    public static TheoryData<string> EstadosFinais => ["concluida", "falha", "expirada"];

    [Theory]
    [MemberData(nameof(EstadosFinais))]
    public void Estado_final_nao_aceita_nenhuma_transicao(string final)
    {
        var t = Nova();
        switch (final)
        {
            case "concluida":
                t.Confirmar(MetodoConfirmacao.Face);
                t.Concluir(Agora);
                break;
            case "falha":
                t.Falhar(MotivoFalha.SenhaRecusada);
                break;
            default:
                t.Expirar();
                break;
        }

        Assert.Throws<InvalidOperationException>(() => t.Confirmar(MetodoConfirmacao.Face));
        Assert.Throws<InvalidOperationException>(() => t.Concluir(Agora));
        Assert.Throws<InvalidOperationException>(() => t.Falhar("OUTRO"));
        Assert.Throws<InvalidOperationException>(t.Expirar);
    }

    [Fact]
    public void Processando_nao_expira_nem_confirma_de_novo()
    {
        var t = Nova();
        t.Confirmar(MetodoConfirmacao.Face);

        Assert.Throws<InvalidOperationException>(t.Expirar);
        Assert.Throws<InvalidOperationException>(() => t.Confirmar(MetodoConfirmacao.Senha));
    }

    [Fact]
    public void Credito_inicial_nasce_concluido()
    {
        var t = Transferencia.CreditoInicial(Guid.NewGuid(), Guid.NewGuid(), Dinheiro.DeReais(1000), Agora);

        Assert.Equal(StatusTransferencia.Concluida, t.Status);
        Assert.Equal(TipoTransferencia.CreditoInicial, t.Tipo);
        Assert.NotNull(t.E2eId);
    }

    [Fact]
    public void Recusa_origem_igual_ao_destino_valor_zero_e_descricao_longa()
    {
        var conta = Guid.NewGuid();

        Assert.Throws<ArgumentException>(() => Transferencia.Criar(conta, conta, Dinheiro.DeCentavos(1), null, Agora));
        Assert.Throws<ArgumentOutOfRangeException>(() => Transferencia.Criar(conta, Guid.NewGuid(), Dinheiro.Zero, null, Agora));
        Assert.Throws<ArgumentException>(() => Transferencia.Criar(conta, Guid.NewGuid(), Dinheiro.DeCentavos(1), new string('x', 141), Agora));
    }

    [Fact]
    public void Falha_exige_motivo()
    {
        Assert.Throws<ArgumentException>(() => Nova().Falhar(" "));
    }
}

public class ContaSaldoTests
{
    private static readonly DateTimeOffset Agora = DateTimeOffset.UtcNow;

    [Fact]
    public void Conta_de_cliente_nao_debita_acima_do_saldo()
    {
        var conta = Conta.AbrirParaCliente(Guid.NewGuid(), "100", Agora);
        conta.Creditar(Dinheiro.DeReais(100));

        conta.Debitar(Dinheiro.DeReais(100));
        Assert.Equal(0, conta.SaldoCentavos);
        Assert.Throws<InvalidOperationException>(() => conta.Debitar(Dinheiro.DeCentavos(1)));
        Assert.Equal(0, conta.SaldoCentavos);
    }

    [Fact]
    public void Conta_de_sistema_pode_ficar_negativa()
    {
        var sistema = Conta.AbrirDoSistema("1", Agora);

        sistema.Debitar(Dinheiro.DeReais(1000));

        Assert.Equal(-100_000, sistema.SaldoCentavos);
    }

    [Fact]
    public void Lancamento_exige_valor_positivo()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Lancamento.Registrar(Guid.NewGuid(), Guid.NewGuid(), TipoLancamento.Debito, 0, 0, Agora));
    }
}
