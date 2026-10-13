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

public sealed class TransferenciaNaoEncontradaException() : NegocioException("NOT_FOUND", "Transferência não encontrada.");

public sealed class MesmaContaException() : NegocioException("SAME_ACCOUNT", "Origem e destino são a mesma conta.");

public sealed class DestinoIndisponivelException() : NegocioException("DESTINATION_UNAVAILABLE", "A conta de destino não pode receber agora.");

public sealed class SaldoInsuficienteException() : NegocioException("INSUFFICIENT_FUNDS", "Saldo insuficiente para esta transferência.");

public sealed class SenhaNaoVerificadaException() : NegocioException("PASSWORD_NOT_VERIFIED", "Senha não confere. A transferência foi cancelada.");

public sealed class TransferenciaNaoPendenteException() : NegocioException("TRANSFER_NOT_PENDING", "Esta transferência já foi confirmada, cancelada ou expirou.");

public sealed class TransferenciaExpiradaException() : NegocioException("TRANSFER_EXPIRED", "O tempo para confirmar acabou. Refaça a transferência.");

public sealed class ChaveDeIdempotenciaObrigatoriaException() : NegocioException("IDEMPOTENCY_KEY_REQUIRED", "Envie o cabeçalho Idempotency-Key (de 8 a 255 caracteres).");

public sealed class ChaveDeIdempotenciaReusadaException() : NegocioException("IDEMPOTENCY_KEY_REUSED", "Esta Idempotency-Key já foi usada em outra operação.");

public sealed class RequisicaoEmAndamentoException() : NegocioException("REQUEST_IN_PROGRESS", "Uma chamada com esta Idempotency-Key ainda está em andamento.");

public sealed class LimiteNoturnoExcedidoException() : NegocioException("NIGHT_LIMIT_EXCEEDED", "Esta transferência passa do limite noturno da conta.");

public sealed class LimiteDiurnoExcedidoException() : NegocioException("DAILY_LIMIT_EXCEEDED", "Esta transferência passa do limite diário da conta.");

/// <summary>Aumentar limite exige rosto ou senha. Sem o desafio implementado, a rota recusa o aumento com este código.</summary>
public sealed class ConfirmacaoNecessariaException(string finalidade) : NegocioException("STEP_UP_REQUIRED", "Esta alteração exige confirmação por rosto ou senha.")
{
    public string Finalidade { get; } = finalidade;
}
