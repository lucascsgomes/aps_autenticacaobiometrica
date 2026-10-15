namespace Ministerio.Dominio;

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
public sealed class ContaBloqueadaException(TimeSpan tentarEm) : NegocioException("ACCOUNT_LOCKED", "Acesso temporariamente bloqueado por falhas seguidas.")
{
    public TimeSpan TentarEm { get; } = tentarEm;
}

public sealed class NaoAutenticadoException() : NegocioException("UNAUTHORIZED", "Sessão inválida ou expirada.");

public sealed class SenhaNaoVerificadaException() : NegocioException("PASSWORD_NOT_VERIFIED", "Senha não confere.");

/// <summary>Acessar o nível N exige o nível N ou acima, e o rosto confirmado na hora.</summary>
public sealed class NivelInsuficienteException(int exigido) : NegocioException("NIVEL_INSUFICIENTE", $"Esta informação é de nível {exigido}, acima do seu nível de acesso.")
{
    public int Exigido { get; } = exigido;
}

/// <summary>Os níveis restritos só abrem com o rosto: a senha não vale.</summary>
public sealed class ExigeRostoException() : NegocioException("FACE_REQUIRED", "Este nível só abre com a confirmação do rosto.");

public sealed class ConfirmacaoNecessariaException(string finalidade) : NegocioException("STEP_UP_REQUIRED", "Esta ação exige confirmação por rosto ou senha.")
{
    public string Finalidade { get; } = finalidade;
}

public sealed class BiometriaNaoCadastradaException() : NegocioException("BIOMETRIA_NOT_REGISTERED", "Esta conta não tem rosto cadastrado.");

public sealed class RostoNaoDetectadoException() : NegocioException("NO_FACE_DETECTED", "Não encontramos um rosto na foto. Enquadre o rosto e tente de novo.");

public sealed class VariosRostosException() : NegocioException("MULTIPLE_FACES", "Há mais de um rosto na foto. Fique sozinho no enquadramento.");

public sealed class ImagemInvalidaException() : NegocioException("INVALID_IMAGE", "Não foi possível ler a imagem enviada.");

public sealed class ImagemGrandeException() : NegocioException("IMAGE_TOO_LARGE", "A imagem passa de 2 MB.");

/// <summary>Falha fechada: sem o serviço facial o rosto nunca é aceito. A senha continua valendo onde a rota permite.</summary>
public sealed class ServicoFacialIndisponivelException() : NegocioException("FACE_SERVICE_UNAVAILABLE", "O reconhecimento facial está indisponível agora. Tente de novo em instantes.");

/// <summary>Desafio inexistente, vencido ou usado, usuário sem rosto, e rosto que não bate: tudo responde igual.</summary>
public sealed class FaceNaoVerificadaException() : NegocioException("FACE_NOT_VERIFIED", "Não foi possível confirmar o rosto.");

public sealed class TokenDeCadastroEncerradoException() : NegocioException("FACE_TOKEN_EXPIRED", "Este cadastro não está mais aguardando o rosto.");
