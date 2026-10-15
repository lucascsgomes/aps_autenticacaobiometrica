using Ministerio.Dominio.Acesso;
using Ministerio.Dominio.Biometria;
using Ministerio.Dominio.Usuarios;
using Microsoft.EntityFrameworkCore;

namespace Ministerio.Infra.Persistencia;

public sealed class MinisterioDbContext(DbContextOptions<MinisterioDbContext> opcoes) : DbContext(opcoes)
{
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<TemplateFacial> Templates => Set<TemplateFacial>();
    public DbSet<DesafioBiometrico> Desafios => Set<DesafioBiometrico>();
    public DbSet<PropriedadeRural> Propriedades => Set<PropriedadeRural>();
    public DbSet<RegistroDeAcesso> Acessos => Set<RegistroDeAcesso>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MinisterioDbContext).Assembly);
}
