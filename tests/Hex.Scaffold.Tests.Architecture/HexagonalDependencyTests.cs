using System.Reflection;

namespace Hex.Scaffold.Tests.Architecture;

public class HexagonalDependencyTests
{
  private static readonly Assembly DomainAssembly =
    typeof(Hex.Scaffold.Domain.AccountAggregate.Account).Assembly;

  private static readonly Assembly ApplicationAssembly =
    typeof(Hex.Scaffold.Application.Accounts.AccountDto).Assembly;

  private static readonly Assembly InboundAssembly =
    typeof(Hex.Scaffold.Adapters.Inbound.Api.Accounts.CreateAccount).Assembly;

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
  public void CreateWithExternalId_LivesInDomainLayer()
  {
    // The replay-safe factory used by the Kafka inbound load tests must live
    // in the Domain layer — it's a domain concern (aggregate construction
    // with a caller-supplied identity), not an Application or Adapter one.
    var method = typeof(Hex.Scaffold.Domain.AccountAggregate.Account)
      .GetMethod(
        nameof(Hex.Scaffold.Domain.AccountAggregate.Account.CreateWithExternalId),
        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

    method.ShouldNotBeNull("Account.CreateWithExternalId factory is missing");
    method!.DeclaringType!.Assembly.ShouldBe(DomainAssembly,
      "CreateWithExternalId must be declared in the Domain assembly");
  }

  [Fact]
  [Trait("Category", "Architecture")]
  public void Account_Create_And_CreateWithExternalId_Both_Raise_AccountCreatedEvent()
  {
    // Both factories must register exactly one AccountCreatedEvent so the
    // PR #20 invariant + at-least-once delivery semantics behave identically
    // regardless of which path was used to construct the aggregate.
    var viaCreate = Hex.Scaffold.Domain.AccountAggregate.Account.Create(
      livemode: false,
      displayName: "test",
      contactEmail: null,
      contactPhone: null,
      appliedConfigurations: null,
      configurationJson: null,
      identityJson: null,
      defaultsJson: null,
      metadataJson: null);

    var externalId = Hex.Scaffold.Domain.AccountAggregate.AccountId.From(
      Hex.Scaffold.Domain.AccountAggregate.AccountId.Prefix + "TESTLOADTEST0000000001");
    var viaExternal = Hex.Scaffold.Domain.AccountAggregate.Account.CreateWithExternalId(
      id: externalId,
      livemode: false,
      displayName: "test",
      contactEmail: null,
      contactPhone: null,
      appliedConfigurations: null,
      configurationJson: null,
      identityJson: null,
      defaultsJson: null,
      metadataJson: null);

    viaCreate.DomainEvents.Count.ShouldBe(1, "Account.Create must raise exactly one event");
    viaCreate.DomainEvents[0].ShouldBeOfType<Hex.Scaffold.Domain.AccountAggregate.Events.AccountCreatedEvent>();

    viaExternal.DomainEvents.Count.ShouldBe(1, "CreateWithExternalId must raise exactly one event");
    viaExternal.DomainEvents[0].ShouldBeOfType<Hex.Scaffold.Domain.AccountAggregate.Events.AccountCreatedEvent>();

    viaExternal.Id.ShouldBe(externalId, "CreateWithExternalId must honour the supplied id (PR #20 invariant)");
  }

  [Fact]
  [Trait("Category", "Architecture")]
  public void DomainEntities_ShouldHaveOnlyPrivateSetters()
  {
    // Check only properties declared on Account itself (not inherited
    // EntityBase members like Id which has a public setter for ORM
    // rehydration). All Account state mutations go through ApplyUpdate /
    // Close / Create — declared properties must not have public setters.
    var accountType = typeof(Hex.Scaffold.Domain.AccountAggregate.Account);
    var publicSetters = accountType
      .GetProperties(System.Reflection.BindingFlags.DeclaredOnly
        | System.Reflection.BindingFlags.Public
        | System.Reflection.BindingFlags.Instance)
      .Where(p => p.CanWrite && (p.SetMethod?.IsPublic == true))
      .Select(p => p.Name)
      .ToList();

    publicSetters.ShouldBeEmpty(
      $"Account entity has public setters: {string.Join(", ", publicSetters)}");
  }
}
