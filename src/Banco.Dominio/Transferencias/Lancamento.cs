namespace Banco.Dominio.Transferencias;

/// <summary>Debito reduz o saldo da conta, Credito aumenta.</summary>
public enum TipoLancamento
{
    Debito,
    Credito,
}

/// <summary>Perna do ledger de partida dobrada. Nunca é editada nem apagada (o banco impede).</summary>
public sealed class Lancamento
{
    public long Id { get; private set; }
    public Guid TransferenciaId { get; private set; }
    public Guid ContaId { get; private set; }
    public TipoLancamento Tipo { get; private set; }
    public long ValorCentavos { get; private set; }
    public long SaldoAposCentavos { get; private set; }
    public DateTimeOffset CriadoEm { get; private set; }

    private Lancamento()
    {
    }

    public static Lancamento Registrar(Guid transferenciaId, Guid contaId, TipoLancamento tipo, long valorCentavos, long saldoAposCentavos, DateTimeOffset agora) =>
        valorCentavos <= 0
            ? throw new ArgumentOutOfRangeException(nameof(valorCentavos), "Lançamento exige valor maior que zero.")
            : new Lancamento
            {
                TransferenciaId = transferenciaId,
                ContaId = contaId,
                Tipo = tipo,
                ValorCentavos = valorCentavos,
                SaldoAposCentavos = saldoAposCentavos,
                CriadoEm = agora,
            };
}
