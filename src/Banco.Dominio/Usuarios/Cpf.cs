namespace Banco.Dominio.Usuarios;

/// <summary>Valida CPF só com dígitos (sem pontuação) e os dois dígitos verificadores.</summary>
public static class Cpf
{
    public static bool EhValido(string? cpf)
    {
        if (cpf is not { Length: 11 } || !cpf.All(char.IsAsciiDigit) || cpf.Distinct().Count() == 1)
        {
            return false;
        }

        return cpf[9] - '0' == DigitoVerificador(cpf, 9) && cpf[10] - '0' == DigitoVerificador(cpf, 10);
    }

    private static int DigitoVerificador(string cpf, int quantidade)
    {
        var soma = 0;
        for (var i = 0; i < quantidade; i++)
        {
            soma += (cpf[i] - '0') * (quantidade + 1 - i);
        }

        var resto = soma * 10 % 11;
        return resto == 10 ? 0 : resto;
    }
}
