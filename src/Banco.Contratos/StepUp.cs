using System.Text.Json;

namespace Banco.Contratos;

/// <summary><c>finalidade</c> é ALTERAR_LIMITE ou NOVA_CHAVE; <c>parametros</c> tem a mesma forma do pedido que o desafio vai confirmar.</summary>
public sealed record DesafioDeAcaoRequest(string? Finalidade, JsonElement? Parametros);

public sealed record DesafioDeAcaoResponse(Guid DesafioId, string Finalidade, DateTimeOffset ExpiraEm, string Instrucao);

/// <summary><c>tipo</c> é EMAIL, CELULAR ou ALEATORIA. Na aleatória o servidor gera o valor e <c>valor</c> não vai.</summary>
public sealed record NovaChaveRequest(string? Tipo, string? Valor);

public sealed record ChaveCriadaResponse(Guid Id, string Tipo, string Valor, DateTimeOffset CriadaEm);
