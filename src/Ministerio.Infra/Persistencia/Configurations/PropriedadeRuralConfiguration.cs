using Ministerio.Dominio.Acesso;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ministerio.Infra.Persistencia.Configurations;

internal sealed class PropriedadeRuralConfiguration : IEntityTypeConfiguration<PropriedadeRural>
{
    public void Configure(EntityTypeBuilder<PropriedadeRural> p)
    {
        p.ToTable("propriedades_rurais", t =>
        {
            t.HasCheckConstraint("ck_propriedade_uf", "uf ~ '^[A-Z]{2}$'");
            t.HasCheckConstraint("ck_propriedade_valores", "quantidade_litros >= 0 AND area_hectares > 0 AND multa_centavos >= 0");
        });

        p.HasKey(x => x.Id);
        p.Property(x => x.Municipio).IsRequired();
        p.Property(x => x.Uf).HasColumnType("char(2)").IsRequired();
        p.Property(x => x.Agrotoxico).IsRequired();
        p.Property(x => x.Propriedade).IsRequired();
        p.Property(x => x.Proprietario).IsRequired();
        p.Property(x => x.Documento).IsRequired();
        p.Property(x => x.Situacao).IsRequired();
        p.Property(x => x.QuantidadeLitros).HasColumnType("numeric(12,2)");
        p.Property(x => x.AreaHectares).HasColumnType("numeric(12,2)");
        p.HasIndex(x => new { x.Uf, x.Municipio }).HasDatabaseName("ix_propriedades_local");

        p.HasData(CargaFicticia.Propriedades());
    }
}
