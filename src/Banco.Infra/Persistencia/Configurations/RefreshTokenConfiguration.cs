using Banco.Dominio.Usuarios;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Banco.Infra.Persistencia.Configurations;

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> r)
    {
        r.ToTable("refresh_tokens", t => t.HasCheckConstraint("ck_refresh_hash", "octet_length(token_hash) = 32"));

        r.HasKey(x => x.Id);
        r.Property(x => x.TokenHash).IsRequired();

        r.HasOne<Usuario>().WithMany().HasForeignKey(x => x.UsuarioId).OnDelete(DeleteBehavior.Restrict);
        r.HasOne<RefreshToken>().WithMany().HasForeignKey(x => x.SubstituidoPor).OnDelete(DeleteBehavior.Restrict);

        r.HasIndex(x => x.TokenHash).IsUnique().HasDatabaseName("uq_refresh_hash");
        r.HasIndex(x => x.FamiliaId).HasDatabaseName("ix_refresh_familia");
        r.HasIndex(x => x.UsuarioId).HasFilter("revogado_em IS NULL").HasDatabaseName("ix_refresh_usuario");
    }
}
