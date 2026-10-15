using System.Text.Json;

namespace Ministerio.Contratos;

public sealed record BiometriaResponse(bool Cadastrada, DateTimeOffset? CadastradaEm, string? Modelo);

public sealed record TrocarSenhaRequest(string? NovaSenha);

public sealed record ConfirmarSenhaRequest(string? Senha);

/// <summary><c>finalidade</c> é ACESSO_NIVEL_2, ACESSO_NIVEL_3, TROCAR_SENHA ou RECADASTRO (nenhuma leva parâmetros).</summary>
public sealed record DesafioDeAcaoRequest(string? Finalidade, JsonElement? Parametros);

public sealed record DesafioDeAcaoResponse(Guid DesafioId, string Finalidade, DateTimeOffset ExpiraEm, string Instrucao);
