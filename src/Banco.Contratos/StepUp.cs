using System.Text.Json;

namespace Banco.Contratos;

public sealed record BiometriaResponse(bool Cadastrada, DateTimeOffset? CadastradaEm, string? Modelo);

public sealed record TrocarSenhaRequest(string? NovaSenha);

public sealed record ConfirmarSenhaRequest(string? Senha);

/// <summary><c>finalidade</c> é ALTERAR_LIMITE, NOVA_CHAVE, TROCAR_SENHA ou RECADASTRO (estas duas sem parâmetros); <c>parametros</c> tem a mesma forma do pedido que o desafio vai confirmar.</summary>
public sealed record DesafioDeAcaoRequest(string? Finalidade, JsonElement? Parametros);

public sealed record DesafioDeAcaoResponse(Guid DesafioId, string Finalidade, DateTimeOffset ExpiraEm, string Instrucao);

/// <summary><c>tipo</c> é EMAIL, CELULAR ou ALEATORIA. Na aleatória o servidor gera o valor e <c>valor</c> não vai.</summary>
public sealed record NovaChaveRequest(string? Tipo, string? Valor);

public sealed record ChaveCriadaResponse(Guid Id, string Tipo, string Valor, DateTimeOffset CriadaEm);
