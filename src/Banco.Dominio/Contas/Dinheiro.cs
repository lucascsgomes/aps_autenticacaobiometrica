namespace Banco.Dominio.Contas;

/// <summary>Valor monetário em centavos de real. Nunca usa ponto flutuante.</summary>
public readonly record struct Dinheiro : IComparable<Dinheiro>
{
    public long Centavos { get; }

    private Dinheiro(long centavos) => Centavos = centavos;

    public static Dinheiro Zero => new(0);

    public static Dinheiro DeCentavos(long centavos) =>
        centavos < 0
            ? throw new ArgumentOutOfRangeException(nameof(centavos), "Valor não pode ser negativo.")
            : new(centavos);

    public static Dinheiro DeReais(decimal reais)
    {
        var centavos = reais * 100m;
        return centavos != decimal.Truncate(centavos)
            ? throw new ArgumentException("Valor não pode ter mais de duas casas decimais.", nameof(reais))
            : DeCentavos((long)centavos);
    }

    public static Dinheiro operator +(Dinheiro a, Dinheiro b) => new(checked(a.Centavos + b.Centavos));

    public static Dinheiro operator -(Dinheiro a, Dinheiro b) =>
        a.Centavos < b.Centavos
            ? throw new InvalidOperationException("Saldo insuficiente.")
            : new(a.Centavos - b.Centavos);

    public static bool operator <(Dinheiro a, Dinheiro b) => a.Centavos < b.Centavos;
    public static bool operator >(Dinheiro a, Dinheiro b) => a.Centavos > b.Centavos;
    public static bool operator <=(Dinheiro a, Dinheiro b) => a.Centavos <= b.Centavos;
    public static bool operator >=(Dinheiro a, Dinheiro b) => a.Centavos >= b.Centavos;

    public int CompareTo(Dinheiro outro) => Centavos.CompareTo(outro.Centavos);

    public override string ToString() => (Centavos / 100m).ToString("C2", new System.Globalization.CultureInfo("pt-BR"));
}
