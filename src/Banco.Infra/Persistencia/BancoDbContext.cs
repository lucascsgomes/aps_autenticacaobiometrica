using Banco.Dominio.Contas;
using Banco.Dominio.Usuarios;
using Microsoft.EntityFrameworkCore;

namespace Banco.Infra.Persistencia;

public sealed class BancoDbContext(DbContextOptions<BancoDbContext> opcoes) : DbContext(opcoes)
{
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<Conta> Contas => Set<Conta>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BancoDbContext).Assembly);
}
