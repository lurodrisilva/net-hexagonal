using Hex.Scaffold.Domain.SampleAggregate;

namespace Hex.Scaffold.Adapters.Persistence.PostgreSql;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
  public DbSet<Sample> Samples => Set<Sample>();

  protected override void OnModelCreating(ModelBuilder modelBuilder)
  {
    base.OnModelCreating(modelBuilder);
    modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
  }
}
