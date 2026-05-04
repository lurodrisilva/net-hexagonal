using System.Reflection;
using Hex.Scaffold.Adapters.Persistence.Common;
using Hex.Scaffold.Adapters.Persistence.MongoDb;
using Hex.Scaffold.Application.Accounts.List;
using Hex.Scaffold.Domain.AccountAggregate;
using Hex.Scaffold.Domain.Common;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace Hex.Scaffold.Adapters.Persistence.Extensions;

public static class MongoDbServiceExtensions
{
  // BSON serializer + class-map registration is process-global. Tests that
  // re-build the host inside one AppDomain hit BsonSerializationException
  // ("There is already a serializer registered…") on the second pass; the
  // sentinel makes the registration idempotent.
  private static int _bsonRegistered;

  public static IServiceCollection AddMongoDbServices(
    this IServiceCollection services,
    IConfiguration configuration,
    ILogger logger)
  {
    var connectionString = configuration["MongoDB:ConnectionString"]
      ?? throw new InvalidOperationException("MongoDB:ConnectionString is required.");
    var databaseName = configuration["MongoDB:DatabaseName"] ?? "hex-scaffold";

    if (Interlocked.Exchange(ref _bsonRegistered, 1) == 0)
    {
      RegisterBsonMappings();
    }

    services.AddSingleton<IMongoClient>(_ =>
    {
      var settings = MongoClientSettings.FromConnectionString(connectionString);
      settings.ServerSelectionTimeout = TimeSpan.FromSeconds(5);
      settings.ConnectTimeout = TimeSpan.FromSeconds(10);
      // Pool sizing — keep aligned with the Postgres MaxPoolSize=100 ceiling
      // we use in load tests. The earlier 200 ceiling was too aggressive: at
      // 12 app pods × 200 = 2400 potential connections, M30/M40 connection
      // ceilings (~800/~1600) would saturate and surface as request-pipeline
      // queueing rather than DB latency. 100 keeps the math sane:
      // 12 pods × 100 = 1200 fits inside M40+ (~1600+) and never overcommits
      // smaller tiers either.
      settings.MaxConnectionPoolSize = 100;
      settings.MinConnectionPoolSize = 10;
      settings.MaxConnectionIdleTime = TimeSpan.FromMinutes(10);
      return new MongoClient(settings);
    });

    services.AddSingleton<IMongoDatabase>(sp =>
      sp.GetRequiredService<IMongoClient>().GetDatabase(databaseName));

    services.AddSingleton<IMongoCollection<Account>>(sp =>
    {
      var db = sp.GetRequiredService<IMongoDatabase>();
      var coll = db.GetCollection<Account>("accounts");
      EnsureIndexes(coll);
      return coll;
    });

    // Account-specific repository (open-generic registration not used —
    // Account is the only aggregate that lives here).
    services.AddScoped<IDomainEventDispatcher, MediatorDomainEventDispatcher>();
    services.AddScoped<IRepository<Account>, MongoAccountRepository>();
    services.AddScoped<IReadRepository<Account>, MongoAccountRepository>();
    services.AddScoped<IListAccountsQueryService, MongoListAccountsQueryService>();

    logger.LogInformation("MongoDB services registered (Account repository wired).");
    return services;
  }

  // BSON wire format for the Account aggregate. The class-map matches the
  // private parameterized constructor (which EF Core also uses for Postgres
  // rehydration) so we don't have to expose a public Hydrate factory and
  // can keep the aggregate's invariants closed.
  private static void RegisterBsonMappings()
  {
    BsonSerializer.RegisterSerializer(typeof(AccountId), new AccountIdBsonSerializer());

    if (BsonClassMap.IsClassMapRegistered(typeof(Account))) return;

    // `Id` is declared on EntityBase<Account, AccountId>, not on Account
    // itself. Calling `cm.MapIdProperty(a => a.Id)` on a class map for
    // Account trips the BSON `EnsureMemberInfoIsForThisClass` guard with:
    //   "memberInfo must be for class Account, but was for class EntityBase`2"
    // Standard fix: register the base class first; the derived class map
    // inherits the Id mapping.
    if (!BsonClassMap.IsClassMapRegistered(typeof(EntityBase<Account, AccountId>)))
    {
      BsonClassMap.RegisterClassMap<EntityBase<Account, AccountId>>(cm =>
      {
        cm.MapIdProperty(e => e.Id);
      });
    }

    var ctor = typeof(Account)
      .GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
      .FirstOrDefault(c => c.GetParameters().Length == 15)
      ?? throw new InvalidOperationException(
        "Account's 15-parameter private constructor not found — " +
        "BSON deserialization requires it for hydration.");

    BsonClassMap.RegisterClassMap<Account>(cm =>
    {
      // Id is inherited from EntityBase<,> registration above.
      cm.MapProperty(a => a.Livemode);
      cm.MapProperty(a => a.Created);
      cm.MapProperty(a => a.Closed);
      cm.MapProperty(a => a.DisplayName);
      cm.MapProperty(a => a.ContactEmail);
      cm.MapProperty(a => a.ContactPhone);
      cm.MapProperty(a => a.Dashboard);
      cm.MapProperty(a => a.AppliedConfigurations);
      cm.MapProperty(a => a.ConfigurationJson);
      cm.MapProperty(a => a.IdentityJson);
      cm.MapProperty(a => a.DefaultsJson);
      cm.MapProperty(a => a.RequirementsJson);
      cm.MapProperty(a => a.FutureRequirementsJson);
      cm.MapProperty(a => a.MetadataJson);

      // Names here are property names (above), in the order matching the
      // private ctor parameters of Account. MongoDB invokes the private
      // ctor via reflection.
      cm.MapConstructor(ctor,
        nameof(Account.Id),
        nameof(Account.Livemode),
        nameof(Account.Created),
        nameof(Account.Closed),
        nameof(Account.DisplayName),
        nameof(Account.ContactEmail),
        nameof(Account.ContactPhone),
        nameof(Account.Dashboard),
        nameof(Account.AppliedConfigurations),
        nameof(Account.ConfigurationJson),
        nameof(Account.IdentityJson),
        nameof(Account.DefaultsJson),
        nameof(Account.RequirementsJson),
        nameof(Account.FutureRequirementsJson),
        nameof(Account.MetadataJson));
    });
  }

  private static void EnsureIndexes(IMongoCollection<Account> collection)
  {
    // List endpoint pages by Created descending. Without an explicit index
    // the query falls back to a collection scan + in-memory sort, which
    // dominates p95 once the collection grows past a few thousand docs.
    var idx = new CreateIndexModel<Account>(
      Builders<Account>.IndexKeys.Descending(a => a.Created),
      new CreateIndexOptions { Name = "ix_account_created_desc" });
    collection.Indexes.CreateOne(idx);
  }
}
