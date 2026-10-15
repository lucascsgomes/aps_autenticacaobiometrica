using System.Text.RegularExpressions;
using Ministerio.Contratos;
using Ministerio.Dominio.Usuarios;
using Microsoft.AspNetCore.Mvc;

namespace Ministerio.Api.Endpoints;

internal static partial class Validacao
{
    public const int SenhaMinima = 10;
    public const int SenhaMaxima = 128;

    public static Dictionary<string, string[]> Cadastro(RegistrarRequest? r)
    {
        var erros = new Dictionary<string, string[]>();
        if (r is null)
        {
            erros["corpo"] = ["Envie os dados do cadastro em JSON."];
            return erros;
        }

        if (r.Nome?.Trim().Length is not (>= 3 and <= 120))
        {
            erros["nome"] = ["Informe o nome completo, de 3 a 120 caracteres."];
        }

        ValidarCpf(r.Cpf, erros);

        if (r.Email is null || r.Email.Length > 254 || !EmailSimples().IsMatch(r.Email.Trim()))
        {
            erros["email"] = ["Informe um e-mail válido."];
        }

        if (r.Celular is not null && !Celular().IsMatch(r.Celular))
        {
            erros["celular"] = ["Use o formato +55DDDNÚMERO, só dígitos."];
        }

        ValidarSenha(r.Senha, erros);

        if (r.ConsentimentoBiometrico is not { Aceito: true, VersaoTermo.Length: > 0 and <= 40 })
        {
            erros["consentimento_biometrico"] = ["O consentimento para uso da biometria é obrigatório (aceito e versão do termo)."];
        }

        return erros;
    }

    public static Dictionary<string, string[]> Login(LoginSenhaRequest? r)
    {
        var erros = new Dictionary<string, string[]>();
        ValidarCpf(r?.Cpf, erros, formatoApenas: true);
        if (string.IsNullOrEmpty(r?.Senha) || r.Senha.Length > SenhaMaxima)
        {
            erros["senha"] = ["Informe a senha."];
        }

        return erros;
    }

    public static Dictionary<string, string[]> SoCpf(string? cpf)
    {
        var erros = new Dictionary<string, string[]>();
        ValidarCpf(cpf, erros, formatoApenas: true);
        return erros;
    }

    public static Dictionary<string, string[]> Retomar(RetomarRequest? r)
    {
        var erros = Login(r is null ? null : new LoginSenhaRequest(r.Cpf, r.Senha));
        if (r?.Acao is not ("RETOMAR" or "RECOMECAR"))
        {
            erros["acao"] = ["Use RETOMAR ou RECOMECAR."];
        }
        else if (r.Acao == "RECOMECAR")
        {
            foreach (var (campo, mensagens) in Cadastro(r.Dados is null ? null : r.Dados with { Cpf = r.Cpf }))
            {
                erros[$"dados.{campo}"] = mensagens;
            }
        }

        return erros;
    }

    public static IResult Problema(Dictionary<string, string[]> erros) =>
        Results.Problem(new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Dados inválidos",
            Detail = "Um ou mais campos estão inválidos.",
            Type = "https://api.ministerio.exemplo/problemas/validacao",
            Extensions = { ["code"] = "VALIDATION_ERROR", ["errors"] = erros },
        });

    // No login basta o formato: dizer "dígito verificador inválido" revelaria pouco, mas o erro uniforme é mais simples de manter.
    private static void ValidarCpf(string? cpf, Dictionary<string, string[]> erros, bool formatoApenas = false)
    {
        if (cpf is null || !ApenasDigitos().IsMatch(cpf))
        {
            erros["cpf"] = ["Informe só os 11 dígitos, sem pontos nem traço."];
        }
        else if (!formatoApenas && !Cpf.EhValido(cpf))
        {
            erros["cpf"] = ["CPF inválido."];
        }
    }

    public static Dictionary<string, string[]> NovaSenha(string? senha)
    {
        var erros = new Dictionary<string, string[]>();
        ValidarSenha(senha, erros);
        return erros.ToDictionary(e => "nova_senha", e => e.Value);
    }

    private static void ValidarSenha(string? senha, Dictionary<string, string[]> erros)
    {
        if (senha is null || senha.Length < SenhaMinima)
        {
            erros["senha"] = [$"Use ao menos {SenhaMinima} caracteres."];
        }
        else if (senha.Length > SenhaMaxima)
        {
            erros["senha"] = [$"Use no máximo {SenhaMaxima} caracteres."];
        }
    }

    [GeneratedRegex("^[0-9]{11}$")]
    private static partial Regex ApenasDigitos();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+$")]
    private static partial Regex EmailSimples();

    [GeneratedRegex(@"^\+55[0-9]{10,11}$")]
    private static partial Regex Celular();
}
