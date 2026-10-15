using System.Text.RegularExpressions;

namespace Ministerio.Dominio;

/// <summary>O texto estável de um enum na base e na API: PendenteBiometria vira PENDENTE_BIOMETRIA.</summary>
public static partial class Codigo
{
    public static string De<T>(T valor)
        where T : struct, Enum =>
        Separador().Replace(valor.ToString(), "$1_$2").ToUpperInvariant();

    [GeneratedRegex("([a-z])([A-Z])")]
    private static partial Regex Separador();
}
