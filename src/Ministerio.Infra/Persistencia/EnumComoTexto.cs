using Ministerio.Dominio;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Ministerio.Infra.Persistencia;

/// <summary>Guarda o enum como texto em MAIÚSCULAS com sublinhado (CadastroPendente vira CADASTRO_PENDENTE), igual aos CHECKs da base.</summary>
internal sealed class EnumComoTexto<T>()
    : ValueConverter<T, string>(v => Codigo.De(v), t => De(t))
    where T : struct, Enum
{
    private static T De(string texto) =>
        Enum.GetValues<T>().First(v => Codigo.De(v) == texto);
}
