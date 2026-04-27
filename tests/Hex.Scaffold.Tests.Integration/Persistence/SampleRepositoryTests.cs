using Hex.Scaffold.Domain.Common;
using Hex.Scaffold.Adapters.Persistence.PostgreSql;
using Hex.Scaffold.Domain.Ports.Outbound;
using Hex.Scaffold.Domain.SampleAggregate;
using Hex.Scaffold.Domain.SampleAggregate.Specifications;
using Hex.Scaffold.Tests.Integration.Fixtures;

namespace Hex.Scaffold.Tests.Integration.Persistence;

[Collection("IntegrationTests")]
[Trait("Category", "Integration")]
public class SampleRepositoryTests(IntegrationTestFixture fixture) : IAsyncLifetime
{
  private IServiceScope? _scope;
  private IRepository<Sample>? _repository;
  private ISampleIdGenerator? _idGenerator;
  private AppDbContext? _dbContext;

  public async Task InitializeAsync()
  {
    _scope = fixture.Factory!.Services.CreateScope();
    _dbContext = _scope.ServiceProvider.GetRequiredService<AppDbContext>();
    _repository = _scope.ServiceProvider.GetRequiredService<IRepository<Sample>>();
    _idGenerator = _scope.ServiceProvider.GetRequiredService<ISampleIdGenerator>();

    // EnsureCreatedAsync creates schema from model without requiring migration files.
    // Use this in tests instead of MigrateAsync — no migration history needed.
    await _dbContext.Database.EnsureCreatedAsync();
  }

  public Task DisposeAsync()
  {
    _scope?.Dispose();
    return Task.CompletedTask;
  }

  [Fact]
  public async Task AddAsync_WithValidSample_PersistsToDatabase()
  {
    var id = await _idGenerator!.NextAsync();
    var sample = new Sample(id, SampleName.From("Integration Test Sample"));

    var created = await _repository!.AddAsync(sample);

    created.ShouldNotBeNull();
    created.Id.Value.ShouldBeGreaterThan(0);
    created.Name.Value.ShouldBe("Integration Test Sample");
  }

  [Fact]
  public async Task GetByIdAsync_ExistingSample_ReturnsSample()
  {
    var id = await _idGenerator!.NextAsync();
    var sample = new Sample(id, SampleName.From("Find Me Sample"));
    var created = await _repository!.AddAsync(sample);

    var spec = new SampleByIdSpec(created.Id);
    var found = await _repository.FirstOrDefaultAsync(spec);

    found.ShouldNotBeNull();
    found!.Id.ShouldBe(created.Id);
    found.Name.ShouldBe(sample.Name);
  }
}
