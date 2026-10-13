using System.Globalization;

namespace Banco.Web.Servicos;

/// <summary>Formatação de dinheiro e data em português, sem depender da cultura do navegador (o WebAssembly roda em cultura invariante).</summary>
public static class Formato
{
    public static string Reais(long centavos)
    {
        var inteiro = (Math.Abs(centavos) / 100).ToString("N0", CultureInfo.InvariantCulture).Replace(',', '.');
        return $"{(centavos < 0 ? "-" : "")}R$ {inteiro},{Math.Abs(centavos) % 100:00}";
    }

    public static string DataHora(DateTimeOffset data) => data.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);

    /// <summary>Lê "25", "25,5", "25.50" ou "R$ 1.234,56" em centavos. Recusa zero, negativo e mais de duas casas.</summary>
    public static bool TentarCentavos(string? texto, out long centavos)
    {
        centavos = 0;
        var limpo = (texto ?? "").Replace("R$", "", StringComparison.OrdinalIgnoreCase).Replace(" ", "", StringComparison.Ordinal);
        if (limpo.Contains(',', StringComparison.Ordinal))
        {
            limpo = limpo.Replace(".", "", StringComparison.Ordinal);
        }
        else if (limpo.Count(c => c == '.') > 1 || (limpo.Contains('.', StringComparison.Ordinal) && limpo.Length - limpo.IndexOf('.', StringComparison.Ordinal) == 4))
        {
            limpo = limpo.Replace(".", "", StringComparison.Ordinal); // "1.234" e "1.234.567" são milhares
        }

        var partes = limpo.Split(',', '.');
        if (partes.Length > 2 || partes[0].Length is 0 or > 9 || !partes[0].All(char.IsAsciiDigit)
            || (partes.Length == 2 && (partes[1].Length is 0 or > 2 || !partes[1].All(char.IsAsciiDigit))))
        {
            return false;
        }

        var reais = long.Parse(partes[0], CultureInfo.InvariantCulture);
        var resto = partes.Length == 2 ? long.Parse(partes[1].PadRight(2, '0'), CultureInfo.InvariantCulture) : 0;
        centavos = (reais * 100) + resto;
        return centavos > 0;
    }

    public static string SoDigitos(string? texto) => new((texto ?? "").Where(char.IsAsciiDigit).ToArray());
}
