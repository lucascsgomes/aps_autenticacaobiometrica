namespace Ministerio.Dominio.Acesso;

/// <summary>
/// Registro fictício da base: propriedade rural flagrada com um agrotóxico proibido. Cada campo só aparece a partir de um nível
/// (município, UF, produto e ano no nível 1; propriedade, quantidade e área no 2; proprietário, multa e investigação no 3).
/// </summary>
public sealed class PropriedadeRural
{
    public Guid Id { get; set; }
    public string Municipio { get; set; } = default!;
    public string Uf { get; set; } = default!;
    public string Agrotoxico { get; set; } = default!;
    public int Ano { get; set; }

    public string Propriedade { get; set; } = default!;
    public decimal QuantidadeLitros { get; set; }
    public decimal AreaHectares { get; set; }

    public string Proprietario { get; set; } = default!;
    public string Documento { get; set; } = default!;
    public long MultaCentavos { get; set; }
    public string Situacao { get; set; } = default!;
}
