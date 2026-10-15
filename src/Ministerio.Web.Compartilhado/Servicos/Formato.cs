using System.Globalization;

namespace Ministerio.Web.Servicos;

/// <summary>Formatação de dinheiro e data em português, sem depender da cultura do navegador (o WebAssembly roda em cultura invariante).</summary>
public static class Formato
{
    public static string Reais(long centavos)
    {
        var inteiro = (Math.Abs(centavos) / 100).ToString("N0", CultureInfo.InvariantCulture).Replace(',', '.');
        return $"{(centavos < 0 ? "-" : "")}R$ {inteiro},{Math.Abs(centavos) % 100:00}";
    }

    public static string DataHora(DateTimeOffset data) => data.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);

    public static string SoDigitos(string? texto) => new((texto ?? "").Where(char.IsAsciiDigit).ToArray());
}
