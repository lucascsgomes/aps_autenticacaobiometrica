using System.Text.RegularExpressions;

namespace Banco.Dominio.Usuarios;

public enum StatusUsuario
{
    CadastroPendente,
    Ativo,
    Bloqueado,
    Encerrado,
}

public sealed partial class Usuario
{
    public const int LimiteDeFalhas = 5;
    public const int TetoDoBloqueioEmMinutos = 15;

    public Guid Id { get; private set; }
    public string Nome { get; private set; } = default!;
    public string Cpf { get; private set; } = default!;
    public string Email { get; private set; } = default!;
    public string? Celular { get; private set; }
    public string SenhaHash { get; private set; } = default!;
    public StatusUsuario Status { get; private set; }
    public DateTimeOffset? CadastroConcluidoEm { get; private set; }
    public DateTimeOffset? ConsentimentoBiometricoEm { get; private set; }
    public string? VersaoTermoBiometria { get; private set; }
    public short FalhasConsecutivas { get; private set; }
    public DateTimeOffset? BloqueadoAte { get; private set; }
    public DateTimeOffset CriadoEm { get; private set; }
    public DateTimeOffset AtualizadoEm { get; private set; }

    private Usuario()
    {
    }

    /// <summary>Nasce pendente: só vira ativo quando o rosto é cadastrado.</summary>
    public static Usuario Cadastrar(string nome, string cpf, string email, string? celular, string senhaHash, DateTimeOffset agora)
    {
        nome = nome?.Trim() ?? string.Empty;
        email = email?.Trim().ToLowerInvariant() ?? string.Empty;

        if (nome.Length is < 3 or > 120)
        {
            throw new ArgumentException("Nome deve ter de 3 a 120 caracteres.", nameof(nome));
        }

        if (cpf is null || !ApenasOnzeDigitos().IsMatch(cpf))
        {
            throw new ArgumentException("CPF deve ter 11 dígitos, sem pontuação.", nameof(cpf));
        }

        if (!EmailValido().IsMatch(email))
        {
            throw new ArgumentException("E-mail inválido.", nameof(email));
        }

        if (celular is not null && !CelularValido().IsMatch(celular))
        {
            throw new ArgumentException("Celular deve estar no formato +55DDDNÚMERO.", nameof(celular));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(senhaHash);

        return new Usuario
        {
            Id = Guid.NewGuid(),
            Nome = nome,
            Cpf = cpf,
            Email = email,
            Celular = celular,
            SenhaHash = senhaHash,
            Status = StatusUsuario.CadastroPendente,
            CriadoEm = agora,
            AtualizadoEm = agora,
        };
    }

    /// <summary>LGPD art. 11: o consentimento da biometria é específico e fica registrado com a versão do termo.</summary>
    public void RegistrarConsentimentoBiometrico(string versaoTermo, DateTimeOffset agora)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(versaoTermo);
        ConsentimentoBiometricoEm = agora;
        VersaoTermoBiometria = versaoTermo;
        AtualizadoEm = agora;
    }

    public void ConcluirCadastro(DateTimeOffset agora)
    {
        if (Status != StatusUsuario.CadastroPendente)
        {
            throw new InvalidOperationException("Só um cadastro pendente pode ser concluído.");
        }

        Status = StatusUsuario.Ativo;
        CadastroConcluidoEm = agora;
        AtualizadoEm = agora;
    }

    public bool EstaBloqueado(DateTimeOffset agora) => BloqueadoAte > agora;

    /// <summary>A 5ª falha seguida bloqueia por 1 minuto; a partir daí o tempo dobra (2, 4, 8) até o teto de 15 minutos.</summary>
    public void RegistrarFalhaDeCredencial(DateTimeOffset agora)
    {
        if (FalhasConsecutivas < short.MaxValue)
        {
            FalhasConsecutivas++;
        }

        if (FalhasConsecutivas >= LimiteDeFalhas)
        {
            var minutos = Math.Min(TetoDoBloqueioEmMinutos, 1 << Math.Min(FalhasConsecutivas - LimiteDeFalhas, 4));
            BloqueadoAte = agora.AddMinutes(minutos);
        }

        AtualizadoEm = agora;
    }

    public void RegistrarCredencialCorreta(DateTimeOffset agora)
    {
        FalhasConsecutivas = 0;
        BloqueadoAte = null;
        AtualizadoEm = agora;
    }

    [GeneratedRegex("^[0-9]{11}$")]
    private static partial Regex ApenasOnzeDigitos();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+$")]
    private static partial Regex EmailValido();

    [GeneratedRegex(@"^\+55[0-9]{10,11}$")]
    private static partial Regex CelularValido();
}
