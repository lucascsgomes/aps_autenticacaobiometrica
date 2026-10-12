using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Banco.Infra.Persistencia;

/// <summary>Guarda o enum como texto em MAIÚSCULAS com sublinhado (CadastroPendente vira CADASTRO_PENDENTE), igual aos CHECKs do banco.</summary>
internal sealed partial class EnumComoTexto<T>()
    : ValueConverter<T, string>(v => Para(v), t => De(t))
    where T : struct, Enum
{
    private static string Para(T valor) => Separador().Replace(valor.ToString(), "$1_$2").ToUpperInvariant();

    private static T De(string texto) =>
        Enum.GetValues<T>().First(v => Para(v) == texto);

    [GeneratedRegex("([a-z])([A-Z])")]
    private static partial Regex Separador();
}
