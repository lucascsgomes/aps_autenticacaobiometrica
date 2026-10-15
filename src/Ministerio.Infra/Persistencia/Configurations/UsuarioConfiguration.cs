using Ministerio.Dominio.Usuarios;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ministerio.Infra.Persistencia.Configurations;

internal sealed class UsuarioConfiguration : IEntityTypeConfiguration<Usuario>
{
    public void Configure(EntityTypeBuilder<Usuario> u)
    {
        u.ToTable("usuarios", t =>
        {
            t.HasCheckConstraint("ck_usuarios_nome", "length(btrim(nome)) BETWEEN 3 AND 120");
            t.HasCheckConstraint("ck_usuarios_cpf", "cpf ~ '^[0-9]{11}$'");
            t.HasCheckConstraint("ck_usuarios_email", @"email = lower(email) AND email ~ '^[^@\s]+@[^@\s]+$'");
            t.HasCheckConstraint("ck_usuarios_celular", @"celular IS NULL OR celular ~ '^\+55[0-9]{10,11}$'");
            t.HasCheckConstraint("ck_usuarios_status", "status IN ('CADASTRO_PENDENTE', 'ATIVO', 'BLOQUEADO', 'ENCERRADO')");
            t.HasCheckConstraint("ck_usuarios_falhas", "falhas_consecutivas >= 0");
            t.HasCheckConstraint("ck_usuarios_consentimento_par",
                "(consentimento_biometrico_em IS NULL) = (versao_termo_biometria IS NULL)");
            t.HasCheckConstraint("ck_usuarios_cadastro_concluido",
                "(status = 'CADASTRO_PENDENTE') = (cadastro_concluido_em IS NULL)");
        });

        u.HasKey(x => x.Id);
        u.Property(x => x.Nome).IsRequired();
        u.Property(x => x.Cpf).HasColumnType("char(11)").IsRequired();
        u.Property(x => x.Email).IsRequired();
        u.Property(x => x.SenhaHash).IsRequired();
        u.Property(x => x.Status).HasConversion(new EnumComoTexto<StatusUsuario>()).IsRequired();

        u.HasIndex(x => x.Cpf).IsUnique().HasDatabaseName("uq_usuarios_cpf");
        u.HasIndex(x => x.Email).IsUnique().HasDatabaseName("uq_usuarios_email");
    }
}
