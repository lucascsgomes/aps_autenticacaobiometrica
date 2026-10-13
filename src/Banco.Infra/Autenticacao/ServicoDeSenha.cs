using Microsoft.AspNetCore.Identity;

namespace Banco.Infra.Autenticacao;

/// <summary>Hash de senha com o PasswordHasher do Identity (PBKDF2 com sal e iterações embutidos no hash).</summary>
public sealed class ServicoDeSenha
{
    private readonly PasswordHasher<object> _hasher = new();
    private readonly object _alvo = new();

    // Verificar sempre faz o cálculo, mesmo sem usuário, para o tempo não revelar se o CPF existe.
    private readonly string _hashFicticio;

    public ServicoDeSenha() => _hashFicticio = _hasher.HashPassword(_alvo, Guid.NewGuid().ToString("N"));

    public string Hash(string senha) => _hasher.HashPassword(_alvo, senha);

    public bool Verificar(string? hash, string senha)
    {
        var resultado = _hasher.VerifyHashedPassword(_alvo, hash ?? _hashFicticio, senha);
        return hash is not null && resultado != PasswordVerificationResult.Failed;
    }
}
