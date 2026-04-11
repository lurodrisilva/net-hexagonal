namespace Hex.Scaffold.Adapters.Persistence.PostgreSql;

public class EfRepository<T>(AppDbContext dbContext)
  : RepositoryBase<T>(dbContext), IReadRepository<T>, IRepository<T>
  where T : class, IAggregateRoot;
