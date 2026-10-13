using Banco.Dominio.Chaves;
using Banco.Dominio.Contas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Banco.Infra.Persistencia.Configurations;

internal sealed class ChaveTransferenciaConfiguration : IEntityTypeConfiguration<ChaveTransferencia>
{
    public void Configure(EntityTypeBuilder<ChaveTransferencia> c)
    {
        c.ToTable("chaves_transferencia", t =>
        {
            t.HasCheckConstraint("ck_chave_tipo", "tipo IN ('CPF', 'EMAIL', 'CELULAR', 'ALEATORIA')");
            t.HasCheckConstraint("ck_chave_formato", """
                (tipo = 'CPF' AND valor ~ '^[0-9]{11}$') OR
                (tipo = 'EMAIL' AND valor = lower(valor) AND valor ~ '^[^@\s]+@[^@\s]+$') OR
                (tipo = 'CELULAR' AND valor ~ '^\+55[0-9]{10,11}$') OR
                (tipo = 'ALEATORIA' AND valor ~ '^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$')
                """);
        });

        c.HasKey(x => x.Id);
        c.Property(x => x.Tipo).HasConversion(new EnumComoTexto<TipoChave>()).IsRequired();
        c.Property(x => x.Valor).IsRequired();
        c.HasOne<Conta>().WithMany().HasForeignKey(x => x.ContaId).OnDelete(DeleteBehavior.Restrict);

        // O valor é único no banco inteiro: uma chave aponta para uma conta só, qualquer que seja o tipo.
        c.HasIndex(x => x.Valor).IsUnique().HasDatabaseName("uq_chave_valor");
        c.HasIndex(x => x.ContaId).HasDatabaseName("ix_chaves_conta");
    }
}
