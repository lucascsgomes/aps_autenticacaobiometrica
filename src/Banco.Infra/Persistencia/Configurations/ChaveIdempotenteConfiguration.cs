using Banco.Dominio.Usuarios;
using Banco.Infra.Idempotencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Banco.Infra.Persistencia.Configurations;

internal sealed class ChaveIdempotenteConfiguration : IEntityTypeConfiguration<ChaveIdempotente>
{
    public void Configure(EntityTypeBuilder<ChaveIdempotente> k)
    {
        k.ToTable("idempotency_keys", t =>
        {
            t.HasCheckConstraint("ck_idem_chave", "length(chave) BETWEEN 8 AND 255");
            t.HasCheckConstraint("ck_idem_hash", "octet_length(request_hash) = 32");
            t.HasCheckConstraint("ck_idem_estado", "estado IN ('PROCESSANDO', 'CONCLUIDA')");
            t.HasCheckConstraint("ck_idem_resposta", "(estado = 'CONCLUIDA') = (status_code IS NOT NULL)");
        });

        k.HasKey(x => new { x.UsuarioId, x.Chave });
        k.Property(x => x.Escopo).IsRequired();
        k.Property(x => x.Estado).IsRequired();
        k.Property(x => x.ResponseBody).HasColumnType("jsonb");
        k.HasOne<Usuario>().WithMany().HasForeignKey(x => x.UsuarioId).OnDelete(DeleteBehavior.Cascade);
        k.HasIndex(x => x.ExpiraEm).HasDatabaseName("ix_idem_expiracao");
    }
}
