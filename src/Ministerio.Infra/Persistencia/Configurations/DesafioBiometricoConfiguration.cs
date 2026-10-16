using Ministerio.Dominio.Biometria;
using Ministerio.Dominio.Usuarios;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ministerio.Infra.Persistencia.Configurations;

internal sealed class DesafioBiometricoConfiguration : IEntityTypeConfiguration<DesafioBiometrico>
{
    public void Configure(EntityTypeBuilder<DesafioBiometrico> d)
    {
        d.ToTable("desafios_biometricos", t =>
        {
            t.HasCheckConstraint("ck_desafio_finalidade", "finalidade IN ('CADASTRO', 'LOGIN', 'ACESSO_NIVEL2', 'ACESSO_NIVEL3', 'TROCAR_SENHA', 'RECADASTRO', 'ALTERAR_ACESSO', 'ALTERAR_PROCESSAMENTO', 'EXCLUIR_CADASTRO')");
            t.HasCheckConstraint("ck_desafio_metodo_valor", "metodo IN ('FACE', 'SENHA')");
            t.HasCheckConstraint("ck_desafio_nonce", "octet_length(nonce) = 32");
            t.HasCheckConstraint("ck_desafio_hash", "hash_operacao IS NULL OR octet_length(hash_operacao) = 32");
            t.HasCheckConstraint("ck_desafio_limiar", "limiar_aplicado BETWEEN 0.300 AND 0.900");
            t.HasCheckConstraint("ck_desafio_devolvido", "devolvido_vezes BETWEEN 0 AND 5");
            t.HasCheckConstraint("ck_desafio_validade", "expira_em > criado_em AND expira_em <= criado_em + interval '5 minutes'");
            t.HasCheckConstraint("ck_desafio_uso", "usado_em IS NULL OR usado_em >= criado_em");
            t.HasCheckConstraint("ck_desafio_metodo", "(usado_em IS NULL) = (metodo IS NULL)");
            // Todo step-up fora login e cadastro carrega o hash que o amarra à operação.
            t.HasCheckConstraint("ck_desafio_amarrado", "finalidade IN ('LOGIN', 'CADASTRO') OR hash_operacao IS NOT NULL");
            t.HasCheckConstraint("ck_desafio_usuario", "usuario_id IS NOT NULL OR finalidade = 'LOGIN'");
        });

        d.HasKey(x => x.Id);
        d.Property(x => x.Finalidade).HasConversion(new EnumComoTexto<FinalidadeDoDesafio>()).IsRequired();
        d.Property(x => x.Metodo).HasConversion(new EnumComoTexto<MetodoDoDesafio>());
        d.Property(x => x.Nonce).IsRequired();
        d.Property(x => x.LimiarAplicado).HasColumnType("numeric(4,3)");

        d.HasOne<Usuario>().WithMany().HasForeignKey(x => x.UsuarioId).OnDelete(DeleteBehavior.Cascade);

        d.HasIndex(x => x.Nonce).IsUnique().HasDatabaseName("uq_desafio_nonce");
        d.HasIndex(x => x.ExpiraEm).HasFilter("usado_em IS NULL").HasDatabaseName("ix_desafios_expiracao");
        d.HasIndex(x => new { x.UsuarioId, x.CriadoEm }).IsDescending(false, true).HasDatabaseName("ix_desafios_usuario");
    }
}
