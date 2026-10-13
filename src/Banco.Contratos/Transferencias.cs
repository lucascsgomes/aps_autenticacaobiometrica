namespace Banco.Contratos;

public sealed record IntencaoRequest(string? ChaveDestino, long? ValorCentavos, string? Descricao);

public sealed record ConfirmarRequest(string? Senha);

public sealed record OrigemResumoDto(string Nome, string ContaMascarada);

public sealed record DestinoResumoDto(string NomeMascarado, string ChaveMascarada, string Instituicao);

public sealed record ResumoDto(OrigemResumoDto Origem, DestinoResumoDto Destino, long ValorCentavos, long TarifaCentavos, long SaldoAposCentavos);

public sealed record IntencaoResponse(
    Guid TransferenciaId,
    string Status,
    DateTimeOffset ExpiraEm,
    ResumoDto Resumo,
    string Instrucao,
    IReadOnlyList<string> Metodos);

public sealed record ParteDto(string Nome, string Agencia, string ContaMascarada);

public sealed record ComprovanteResponse(
    Guid TransferenciaId,
    string E2eId,
    string Status,
    string Tipo,
    long ValorCentavos,
    long TarifaCentavos,
    string? Descricao,
    ParteDto Origem,
    ParteDto Destino,
    DateTimeOffset ConcluidaEm,
    long SaldoAposCentavos,
    string MetodoConfirmacao,
    string Autenticacao);

public sealed record HistoricoDto(string? De, string Para, DateTimeOffset Em);

public sealed record TransferenciaResponse(
    Guid TransferenciaId,
    string Status,
    long ValorCentavos,
    string? MotivoFalha,
    DateTimeOffset CriadaEm,
    DateTimeOffset? ConcluidaEm,
    IReadOnlyList<HistoricoDto> Historico);
