using Ministerio.Dominio.Acesso;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ministerio.Infra.Persistencia.Configurations;

internal sealed class ConfiguracaoConfiguration : IEntityTypeConfiguration<Configuracao>
{
    public void Configure(EntityTypeBuilder<Configuracao> c)
    {
        c.ToTable("configuracoes");
        c.HasKey(x => x.Chave);
        c.Property(x => x.Chave).HasMaxLength(60);
        c.Property(x => x.Valor).HasMaxLength(60).IsRequired();
    }
}
