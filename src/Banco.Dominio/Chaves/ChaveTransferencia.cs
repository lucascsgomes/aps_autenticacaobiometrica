using System.Text.RegularExpressions;

namespace Banco.Dominio.Chaves;

public enum TipoChave
{
    Cpf,
    Email,
    Celular,
    Aleatoria,
}

/// <summary>Apelido que aponta para uma conta, no estilo da chave Pix. O valor é único no banco inteiro.</summary>
public sealed partial class ChaveTransferencia
{
    public Guid Id { get; private set; }
    public Guid ContaId { get; private set; }
    public TipoChave Tipo { get; private set; }
    public string Valor { get; private set; } = default!;
    public DateTimeOffset CriadaEm { get; private set; }

    private ChaveTransferencia()
    {
    }

    public static ChaveTransferencia Criar(Guid contaId, TipoChave tipo, string valor, DateTimeOffset agora) =>
        Classificar(valor) == tipo
            ? new ChaveTransferencia { Id = Guid.NewGuid(), ContaId = contaId, Tipo = tipo, Valor = valor, CriadaEm = agora }
            : throw new ArgumentException("O valor não tem o formato do tipo da chave.", nameof(valor));

    /// <summary>Descobre o tipo pelo formato. Nulo quando não é chave: CPF com pontuação, e-mail com maiúscula e celular sem +55 caem aqui.</summary>
    public static TipoChave? Classificar(string? valor)
    {
        if (valor is null)
        {
            return null;
        }

        if (Onze().IsMatch(valor))
        {
            return Usuarios.Cpf.EhValido(valor) ? TipoChave.Cpf : null;
        }

        if (Celular().IsMatch(valor))
        {
            return TipoChave.Celular;
        }

        if (Uuid4().IsMatch(valor))
        {
            return TipoChave.Aleatoria;
        }

        return valor.Length <= 254 && !valor.Any(char.IsUpper) && Email().IsMatch(valor) ? TipoChave.Email : null;
    }

    [GeneratedRegex("^[0-9]{11}$")]
    private static partial Regex Onze();

    [GeneratedRegex(@"^\+55[0-9]{10,11}$")]
    private static partial Regex Celular();

    [GeneratedRegex("^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$")]
    private static partial Regex Uuid4();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+$")]
    private static partial Regex Email();
}

/// <summary>O que a tela pode mostrar de terceiros: nunca o CPF inteiro, nunca o sobrenome completo.</summary>
public static class Mascara
{
    public static string Nome(string nome)
    {
        var partes = nome.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return partes.Length < 2 ? $"{partes[0]}***" : $"{partes[0]} {partes[^1][0]}***";
    }

    public static string Chave(TipoChave tipo, string valor) => tipo switch
    {
        TipoChave.Cpf => $"***.{valor[3..6]}.{valor[6..9]}-**",
        TipoChave.Email => $"{valor[0]}***{valor[valor.IndexOf('@', StringComparison.Ordinal)..]}",
        TipoChave.Celular => $"+55*******{valor[^4..]}",
        _ => $"{valor[..8]}-****",
    };

    public static string Conta(string numero) => $"****-{numero[^1]}";
}
