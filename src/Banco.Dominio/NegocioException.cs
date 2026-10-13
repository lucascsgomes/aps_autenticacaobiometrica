namespace Banco.Dominio;

/// <summary>Falha esperada do negócio. A API traduz cada uma para um <c>code</c> estável e um status HTTP.</summary>
public abstract class NegocioException(string code, string mensagem) : Exception(mensagem)
{
    public string Code { get; } = code;
}

/// <summary>CPF ou e-mail já em uso. O texto é genérico de propósito, para não revelar qual dos dois colidiu.</summary>
public sealed class ConflitoDeCadastroException() : NegocioException("REGISTRATION_CONFLICT", "Não foi possível concluir o cadastro com esses dados.");

/// <summary>CPF inexistente, senha errada e cadastro pendente respondem exatamente igual.</summary>
public sealed class CredenciaisInvalidasException() : NegocioException("INVALID_CREDENTIALS", "CPF ou senha inválidos.");

/// <summary>Só é revelado depois de uma credencial correta.</summary>
public sealed class ContaBloqueadaException(TimeSpan tentarEm) : NegocioException("ACCOUNT_LOCKED", "Conta temporariamente bloqueada por falhas seguidas.")
{
    public TimeSpan TentarEm { get; } = tentarEm;
}

public sealed class NaoAutenticadoException() : NegocioException("UNAUTHORIZED", "Sessão inválida ou expirada.");

public sealed class ChaveNaoEncontradaException() : NegocioException("KEY_NOT_FOUND", "Chave não encontrada.");

public sealed class ChaveInvalidaException() : NegocioException("INVALID_KEY_FORMAT", "A chave não tem um formato válido.");
