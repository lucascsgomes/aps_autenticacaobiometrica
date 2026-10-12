namespace Banco.Dominio.Contas;

public enum TipoConta
{
    Cliente,
    Sistema,
}

public enum StatusConta
{
    Ativa,
    Bloqueada,
    Encerrada,
}

public sealed class Conta
{
    public const string AgenciaPadrao = "0001";

    public Guid Id { get; private set; }
    public Guid? UsuarioId { get; private set; }
    public TipoConta Tipo { get; private set; }
    public string Agencia { get; private set; } = AgenciaPadrao;
    public string Numero { get; private set; } = default!;

    /// <summary>Saldo materializado em centavos. Só Debitar e Creditar o alteram, dentro da transação do ledger.</summary>
    public long SaldoCentavos { get; private set; }

    public long LimiteDiurnoCentavos { get; private set; } = 500_000;
    public long LimiteNoturnoCentavos { get; private set; } = 100_000;
    public TimeOnly InicioNoturno { get; private set; } = new(20, 0);
    public TimeOnly FimNoturno { get; private set; } = new(6, 0);
    public StatusConta Status { get; private set; }
    public DateTimeOffset CriadoEm { get; private set; }

    public Dinheiro Saldo => Dinheiro.DeCentavos(SaldoCentavos);

    private Conta()
    {
    }

    public static Conta AbrirParaCliente(Guid usuarioId, string numero, DateTimeOffset agora)
    {
        if (usuarioId == Guid.Empty)
        {
            throw new ArgumentException("Conta de cliente exige usuário.", nameof(usuarioId));
        }

        return Nova(TipoConta.Cliente, usuarioId, numero, agora);
    }

    /// <summary>Contrapartida do crédito inicial fictício: dinheiro não surge do nada no ledger.</summary>
    public static Conta AbrirDoSistema(string numero, DateTimeOffset agora) =>
        Nova(TipoConta.Sistema, null, numero, agora);

    /// <summary>Só a conta de sistema pode ficar negativa: é a contrapartida do dinheiro fictício.</summary>
    public bool PodeDebitar(Dinheiro valor) => Tipo == TipoConta.Sistema || SaldoCentavos >= valor.Centavos;

    public void Debitar(Dinheiro valor) =>
        SaldoCentavos = PodeDebitar(valor)
            ? checked(SaldoCentavos - valor.Centavos)
            : throw new InvalidOperationException("Saldo insuficiente.");

    public void Creditar(Dinheiro valor) => SaldoCentavos = checked(SaldoCentavos + valor.Centavos);

    private static Conta Nova(TipoConta tipo, Guid? usuarioId, string numero, DateTimeOffset agora)
    {
        if (string.IsNullOrWhiteSpace(numero) || !numero.All(char.IsAsciiDigit))
        {
            throw new ArgumentException("Número da conta deve conter apenas dígitos.", nameof(numero));
        }

        return new Conta
        {
            Id = Guid.NewGuid(),
            UsuarioId = usuarioId,
            Tipo = tipo,
            Numero = numero,
            Status = StatusConta.Ativa,
            CriadoEm = agora,
        };
    }
}
