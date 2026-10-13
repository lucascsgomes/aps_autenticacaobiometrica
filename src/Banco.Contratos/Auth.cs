namespace Banco.Contratos;

// Os nomes viram snake_case no JSON (RegistrarRequest.Cpf vira "cpf", ConsentimentoBiometrico vira "consentimento_biometrico").

public sealed record ConsentimentoBiometricoDto(bool Aceito, string? VersaoTermo);

public sealed record RegistrarRequest(
    string? Nome,
    string? Cpf,
    string? Email,
    string? Celular,
    string? Senha,
    ConsentimentoBiometricoDto? ConsentimentoBiometrico);

public sealed record LoginSenhaRequest(string? Cpf, string? Senha);

public sealed record RefreshRequest(string? RefreshToken);

public sealed record UsuarioDto(Guid Id, string Nome);

public sealed record ContaResumoDto(Guid Id, string Agencia, string Numero);

/// <summary>Resposta de qualquer entrada bem-sucedida (cadastro concluído, login, refresh).</summary>
public sealed record SessaoResponse(
    string TokenType,
    string AccessToken,
    int ExpiresIn,
    string RefreshToken,
    int RefreshExpiresIn,
    UsuarioDto Usuario,
    string? Metodo);

/// <summary>Sessão só vem preenchida quando o cadastro já foi concluído (modo de desenvolvimento sem rosto).</summary>
public sealed record RegistrarResponse(
    Guid UsuarioId,
    ContaResumoDto Conta,
    string? ProximoPasso,
    SessaoResponse? Sessao,
    string? CadastroToken = null,
    DateTimeOffset? ExpiraEm = null);

public sealed record TitularDto(string Nome);

public sealed record ContaMeResponse(Guid Id, string Agencia, string Numero, string Status, long SaldoCentavos, TitularDto Titular, IReadOnlyList<ChaveDto> Chaves);

public sealed record LoginDesafioRequest(string? Cpf);

public sealed record DesafioResponse(Guid DesafioId, DateTimeOffset ExpiraEm, string Instrucao);

/// <summary><c>acao</c> é RETOMAR (mantém os dados) ou RECOMECAR (exige <c>dados</c>, no mesmo formato do cadastro).</summary>
public sealed record RetomarRequest(string? Cpf, string? Senha, string? Acao, RegistrarRequest? Dados);

public sealed record RetomarResponse(string CadastroToken, DateTimeOffset ExpiraEm, string ProximoPasso);
