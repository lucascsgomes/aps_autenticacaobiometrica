namespace Banco.Contratos;

public sealed record ChaveDto(Guid Id, string Tipo, string ValorMascarado);

public sealed record ContraparteDto(string Nome, string ContaMascarada);

public sealed record ExtratoItemDto(
    long LancamentoId,
    Guid TransferenciaId,
    string Tipo,
    long ValorCentavos,
    long SaldoAposCentavos,
    ContraparteDto Contraparte,
    string? Descricao,
    string Status,
    DateTimeOffset CriadoEm);

public sealed record ExtratoResponse(IReadOnlyList<ExtratoItemDto> Data, string? NextCursor, bool HasMore);

public sealed record ConsultaChaveResponse(string Tipo, string ChaveMascarada, string NomeMascarado, string Instituicao, bool Propria);
