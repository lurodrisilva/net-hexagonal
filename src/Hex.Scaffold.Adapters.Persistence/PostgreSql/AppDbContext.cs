using Hex.Scaffold.Domain.AccountAggregate;

namespace Hex.Scaffold.Adapters.Persistence.PostgreSql;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
  public DbSet<Account> Accounts => Set<Account>();

  protected override void OnModelCreating(ModelBuilder modelBuilder)
  {
    base.OnModelCreating(modelBuilder);
    modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
  }
}
