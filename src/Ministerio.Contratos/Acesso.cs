namespace Ministerio.Contratos;

public sealed record PerfilResponse(Guid Id, string Nome, string CpfMascarado, int Nivel, string Cargo, bool TemRosto);

/// <summary>
/// Um registro da base ambiental. O servidor só preenche os campos do nível consultado: município, UF, produto e ano no nível 1;
/// propriedade, quantidade e área no 2; proprietário, documento, multa e situação no 3. O resto vai nulo.
/// </summary>
public sealed record PropriedadeDto(
    Guid Id,
    string Municipio,
    string Uf,
    string Agrotoxico,
    int Ano,
    string? Propriedade,
    decimal? QuantidadeLitros,
    decimal? AreaHectares,
    string? Proprietario,
    string? Documento,
    long? MultaCentavos,
    string? Situacao);

public sealed record DadosResponse(int Nivel, IReadOnlyList<PropriedadeDto> Itens);

public sealed record ContagemDto(string Nome, int Total);

public sealed record AcessoDto(DateTimeOffset Quando, string? Usuario, string Evento, string Resultado, string? Motivo, string? Ip);

/// <summary>Relatório de segurança. <c>TentativasIndevidas</c> são pedidos de um nível acima do que a pessoa tem.</summary>
public sealed record RelatorioResponse(
    int Total,
    int Aceitos,
    int Recusados,
    int TentativasIndevidas,
    IReadOnlyList<ContagemDto> PorEvento,
    IReadOnlyList<ContagemDto> PorMotivo,
    IReadOnlyList<AcessoDto> Recentes);
