using System.Reflection;

namespace Hex.Scaffold.Tests.Architecture;

public class HexagonalDependencyTests
{
  private static readonly Assembly DomainAssembly =
    typeof(Hex.Scaffold.Domain.SampleAggregate.Sample).Assembly;

  private static readonly Assembly ApplicationAssembly =
    typeof(Hex.Scaffold.Application.Samples.SampleDto).Assembly;

  private static readonly Assembly InboundAssembly =
    typeof(Hex.Scaffold.Adapters.Inbound.Api.Samples.Create).Assembly;

  private static readonly Assembly OutboundAssembly =
    typeof(Hex.Scaffold.Adapters.Outbound.Messaging.KafkaEventPublisher).Assembly;

  private static readonly Assembly PersistenceAssembly =
    typeof(Hex.Scaffold.Adapters.Persistence.PostgreSql.AppDbContext).Assembly;

  [Fact]
  [Trait("Category", "Architecture")]
  public void Domain_ShouldNotDependOn_Application()
  {
    var result = Types.InAssembly(DomainAssembly)
      .ShouldNot()
      .HaveDependencyOn("Hex.Scaffold.Application")
      .GetResult();

    result.IsSuccessful.ShouldBeTrue(
      $"Domain depends on Application: {string.Join(", ", result.FailingTypeNames ?? [])}");
  }

  [Fact]
  [Trait("Category", "Architecture")]
  public void Domain_ShouldNotDependOn_AnyAdapter()
  {
    var result = Types.InAssembly(DomainAssembly)
      .ShouldNot()
      .HaveDependencyOnAny(
        "Hex.Scaffold.Adapters.Inbound",
        "Hex.Scaffold.Adapters.Outbound",
        "Hex.Scaffold.Adapters.Persistence",
        "Hex.Scaffold.Api")
      .GetResult();

    result.IsSuccessful.ShouldBeTrue(
      $"Domain depends on adapters: {string.Join(", ", result.FailingTypeNames ?? [])}");
  }

  [Fact]
  [Trait("Category", "Architecture")]
  public void Application_ShouldNotDependOn_AnyAdapter()
  {
    var result = Types.InAssembly(ApplicationAssembly)
      .ShouldNot()
      .HaveDependencyOnAny(
        "Hex.Scaffold.Adapters.Inbound",
        "Hex.Scaffold.Adapters.Outbound",
        "Hex.Scaffold.Adapters.Persistence",
        "Hex.Scaffold.Api")
      .GetResult();

    result.IsSuccessful.ShouldBeTrue(
      $"Application depends on adapters: {string.Join(", ", result.FailingTypeNames ?? [])}");
  }

  [Fact]
  [Trait("Category", "Architecture")]
  public void AdaptersInbound_ShouldNotDependOn_OutboundOrPersistence()
  {
    var result = Types.InAssembly(InboundAssembly)
      .ShouldNot()
      .HaveDependencyOnAny(
        "Hex.Scaffold.Adapters.Outbound",
        "Hex.Scaffold.Adapters.Persistence")
      .GetResult();

    result.IsSuccessful.ShouldBeTrue(
      $"Inbound depends on Outbound/Persistence: {string.Join(", ", result.FailingTypeNames ?? [])}");
  }

  [Fact]
  [Trait("Category", "Architecture")]
  public void AdaptersOutbound_ShouldNotDependOn_ApplicationOrInbound()
  {
    var result = Types.InAssembly(OutboundAssembly)
      .ShouldNot()
      .HaveDependencyOnAny(
        "Hex.Scaffold.Application",
        "Hex.Scaffold.Adapters.Inbound",
        "Hex.Scaffold.Adapters.Persistence",
        "Hex.Scaffold.Api")
      .GetResult();

    result.IsSuccessful.ShouldBeTrue(
      $"Outbound depends on forbidden namespaces: {string.Join(", ", result.FailingTypeNames ?? [])}");
  }

  [Fact]
  [Trait("Category", "Architecture")]
  public void AdaptersPersistence_ShouldNotDependOn_InboundOrOutbound()
  {
    var result = Types.InAssembly(PersistenceAssembly)
      .ShouldNot()
      .HaveDependencyOnAny(
        "Hex.Scaffold.Adapters.Inbound",
        "Hex.Scaffold.Adapters.Outbound",
        "Hex.Scaffold.Api")
      .GetResult();

    result.IsSuccessful.ShouldBeTrue(
      $"Persistence depends on forbidden: {string.Join(", ", result.FailingTypeNames ?? [])}");
  }

  [Fact]
  [Trait("Category", "Architecture")]
  public void DomainEntities_ShouldHaveOnlyPrivateSetters()
  {
    // Check only properties declared on Sample itself (not inherited EntityBase members
    // like Id which may have a public setter for ORM purposes).
    var sampleType = typeof(Hex.Scaffold.Domain.SampleAggregate.Sample);
    var publicSetters = sampleType
      .GetProperties(System.Reflection.BindingFlags.DeclaredOnly
        | System.Reflection.BindingFlags.Public
        | System.Reflection.BindingFlags.Instance)
      .Where(p => p.CanWrite && (p.SetMethod?.IsPublic == true))
      .Select(p => p.Name)
      .ToList();

    publicSetters.ShouldBeEmpty(
      $"Sample entity has public setters: {string.Join(", ", publicSetters)}");
  }
}
