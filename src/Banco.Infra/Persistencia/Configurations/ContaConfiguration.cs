using Banco.Dominio.Contas;
using Banco.Dominio.Usuarios;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Banco.Infra.Persistencia.Configurations;

internal sealed class ContaConfiguration : IEntityTypeConfiguration<Conta>
{
    public void Configure(EntityTypeBuilder<Conta> c)
    {
        c.ToTable("contas", t =>
        {
            t.HasCheckConstraint("ck_contas_tipo", "tipo IN ('CLIENTE', 'SISTEMA')");
            t.HasCheckConstraint("ck_contas_status", "status IN ('ATIVA', 'BLOQUEADA', 'ENCERRADA')");
            // Overdraft concorrente falha aqui mesmo que a aplicação erre. Só a conta de sistema pode ficar negativa.
            t.HasCheckConstraint("ck_contas_saldo_nao_negativo", "tipo = 'SISTEMA' OR saldo_centavos >= 0");
            t.HasCheckConstraint("ck_contas_dono", "(tipo = 'CLIENTE') = (usuario_id IS NOT NULL)");
            t.HasCheckConstraint("ck_contas_limite_diurno", "limite_diurno_centavos >= 0");
            t.HasCheckConstraint("ck_contas_limite_noturno", "limite_noturno_centavos >= 0");
            t.HasCheckConstraint("ck_contas_limite_noturno_menor", "limite_noturno_centavos <= limite_diurno_centavos");
            t.HasCheckConstraint("ck_contas_inicio_noturno", "inicio_noturno IN ('20:00', '22:00')");
        });

        c.HasKey(x => x.Id);
        c.Ignore(x => x.Saldo);
        c.Property(x => x.Agencia).HasColumnType("char(4)").IsRequired();
        c.Property(x => x.Numero).IsRequired();
        c.Property(x => x.Tipo).HasConversion(new EnumComoTexto<TipoConta>()).IsRequired();
        c.Property(x => x.Status).HasConversion(new EnumComoTexto<StatusConta>()).IsRequired();

        c.HasOne<Usuario>().WithMany().HasForeignKey(x => x.UsuarioId);

        c.HasIndex(x => new { x.Agencia, x.Numero }).IsUnique().HasDatabaseName("uq_contas_numero");
        // Uma conta de cliente por usuário, e uma única conta de sistema.
        c.HasIndex(x => x.UsuarioId).IsUnique().HasFilter("tipo = 'CLIENTE'").HasDatabaseName("uq_conta_cliente_por_usuario");
        c.HasIndex(x => x.Tipo).IsUnique().HasFilter("tipo = 'SISTEMA'").HasDatabaseName("uq_conta_sistema");
    }
}
