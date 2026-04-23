using Microsoft.EntityFrameworkCore.Design;

namespace Hex.Scaffold.Adapters.Persistence.PostgreSql;

// Used by `dotnet ef migrations bundle/add/update` so the EF tooling can build
// an AppDbContext without bootstrapping the full application host (which would
// pull in every adapter via Scrutor and fail when an adapter's backing service
// — e.g. IMongoClient — is not registered in the selected feature combination).
// At bundle run-time inside the cluster, ConnectionStrings__PostgreSql is
// injected via envFrom from the Kubernetes Secret.
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
  public AppDbContext CreateDbContext(string[] args)
  {
    var connectionString =
      Environment.GetEnvironmentVariable("ConnectionStrings__PostgreSql")
      ?? "Host=localhost;Database=hex-scaffold;Username=postgres;Password=postgres;Port=5432";

    var options = new DbContextOptionsBuilder<AppDbContext>()
      .UseNpgsql(connectionString, npgsql =>
        npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.GetName().Name))
      .Options;

    return new AppDbContext(options);
  }
}
