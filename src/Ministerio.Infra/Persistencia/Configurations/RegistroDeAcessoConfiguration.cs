using Ministerio.Dominio.Acesso;
using Ministerio.Dominio.Usuarios;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ministerio.Infra.Persistencia.Configurations;

internal sealed class RegistroDeAcessoConfiguration : IEntityTypeConfiguration<RegistroDeAcesso>
{
    public void Configure(EntityTypeBuilder<RegistroDeAcesso> r)
    {
        r.ToTable("registros_de_acesso", t =>
        {
            t.HasCheckConstraint("ck_acesso_evento", "evento IN ('LOGIN_SENHA', 'LOGIN_ROSTO', 'LOGIN_IDENTIFICACAO', 'CONSULTA_NIVEL1', 'CONSULTA_NIVEL2', 'CONSULTA_NIVEL3', 'RELATORIO', 'ALTERACAO_DE_ACESSO')");
            t.HasCheckConstraint("ck_acesso_resultado", "resultado IN ('ACEITO', 'RECUSADO')");
            t.HasCheckConstraint("ck_acesso_motivo", "(resultado = 'RECUSADO') = (motivo IS NOT NULL)");
        });

        r.HasKey(x => x.Id);
        r.Property(x => x.Id).UseIdentityAlwaysColumn();
        r.Property(x => x.Evento).HasConversion(new EnumComoTexto<EventoDeAcesso>()).IsRequired();
        r.Property(x => x.Detalhe).HasMaxLength(200);
        r.Property(x => x.Resultado).HasConversion(new EnumComoTexto<ResultadoDoAcesso>()).IsRequired();

        // O histórico sobrevive à exclusão do usuário: o relatório continua dizendo que houve a tentativa.
        r.HasOne<Usuario>().WithMany().HasForeignKey(x => x.UsuarioId).OnDelete(DeleteBehavior.SetNull);
        r.HasIndex(x => x.OcorridoEm).IsDescending().HasDatabaseName("ix_acesso_quando");
    }
}
