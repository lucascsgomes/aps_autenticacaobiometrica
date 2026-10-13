using Banco.Dominio.Biometria;
using Banco.Dominio.Usuarios;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Banco.Infra.Persistencia.Configurations;

internal sealed class TemplateFacialConfiguration : IEntityTypeConfiguration<TemplateFacial>
{
    public void Configure(EntityTypeBuilder<TemplateFacial> t)
    {
        t.ToTable("templates_faciais", c =>
        {
            // 128 floats32 (512 bytes) + tag GCM de 16 bytes. O banco recusa qualquer coisa que não pareça um vetor cifrado.
            c.HasCheckConstraint("ck_template_tamanho", "octet_length(embedding_cifrado) = 528");
            c.HasCheckConstraint("ck_template_nonce", "octet_length(nonce) = 12");
            c.HasCheckConstraint("ck_template_chave_versao", "chave_versao > 0");
        });

        t.HasKey(x => x.Id);
        t.Property(x => x.EmbeddingCifrado).IsRequired();
        t.Property(x => x.Nonce).IsRequired();
        t.Property(x => x.Modelo).IsRequired();
        t.HasOne<Usuario>().WithMany().HasForeignKey(x => x.UsuarioId).OnDelete(DeleteBehavior.Cascade);

        // No máximo um template por usuário. Recadastrar apaga o anterior e insere o novo na mesma transação.
        t.HasIndex(x => x.UsuarioId).IsUnique().HasDatabaseName("uq_template_usuario");
    }
}
