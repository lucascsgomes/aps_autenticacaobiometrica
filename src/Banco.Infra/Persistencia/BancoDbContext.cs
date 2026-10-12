using Banco.Dominio.Contas;
using Banco.Dominio.Transferencias;
using Banco.Dominio.Usuarios;
using Microsoft.EntityFrameworkCore;

namespace Banco.Infra.Persistencia;

public sealed class BancoDbContext(DbContextOptions<BancoDbContext> opcoes) : DbContext(opcoes)
{
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<Conta> Contas => Set<Conta>();
    public DbSet<Transferencia> Transferencias => Set<Transferencia>();
    public DbSet<Lancamento> Lancamentos => Set<Lancamento>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BancoDbContext).Assembly);
}
