using Hex.Scaffold.Application.Accounts.Create;
using Hex.Scaffold.Domain.AccountAggregate;

namespace Hex.Scaffold.Tests.Unit.Application;

[Trait("Category", "Unit")]
public class CreateAccountHandlerTests
{
  private readonly IRepository<Account> _repository = Substitute.For<IRepository<Account>>();
  private readonly IMediator _mediator = Substitute.For<IMediator>();
  private readonly CreateAccountHandler _sut;

  public CreateAccountHandlerTests()
  {
    _sut = new CreateAccountHandler(
      _repository,
      _mediator,
      Substitute.For<ILogger<CreateAccountHandler>>());

    // Default: AddAsync returns whatever was passed in.
    _repository.AddAsync(Arg.Any<Account>(), Arg.Any<CancellationToken>())
      .Returns(call => Task.FromResult(call.Arg<Account>()));
  }

  [Fact]
  public async Task Handle_ReturnsAcctPrefixedId_AndV2ObjectKind()
  {
    var command = new CreateAccountCommand(
      Livemode: false,
      DisplayName: "Furever",
      ContactEmail: "furever@example.com",
      ContactPhone: null,
      AppliedConfigurations: [AppliedConfiguration.Customer, AppliedConfiguration.Merchant],
      ConfigurationJson: null,
      IdentityJson: null,
      DefaultsJson: null,
      MetadataJson: null);

    var result = await _sut.Handle(command, CancellationToken.None);

    result.IsSuccess.ShouldBeTrue();
    result.Value.Id.ShouldStartWith("acct_");
    result.Value.Object.ShouldBe(Account.ObjectKind);
    result.Value.Dashboard.ShouldBe("full");
    result.Value.AppliedConfigurations.ShouldBe(["customer", "merchant"], ignoreOrder: true);
  }

  [Fact]
  public async Task Handle_PersistsRawJsonBlobs_WithoutModification()
  {
    Account? captured = null;
    _repository.AddAsync(Arg.Any<Account>(), Arg.Any<CancellationToken>())
      .Returns(call => { captured = call.Arg<Account>(); return Task.FromResult(captured); });

    var command = new CreateAccountCommand(
      Livemode: false,
      DisplayName: null,
      ContactEmail: null,
      ContactPhone: null,
      AppliedConfigurations: [AppliedConfiguration.Customer],
      ConfigurationJson: """{"customer":{"applied":true}}""",
      IdentityJson: """{"country":"US","entity_type":"company"}""",
      DefaultsJson: null,
      MetadataJson: """{"k":"v"}""");

    await _sut.Handle(command, CancellationToken.None);

    captured.ShouldNotBeNull();
    captured!.ConfigurationJson.ShouldBe("""{"customer":{"applied":true}}""");
    captured.IdentityJson.ShouldBe("""{"country":"US","entity_type":"company"}""");
    captured.MetadataJson.ShouldBe("""{"k":"v"}""");
  }

  [Fact]
  public async Task Handle_PublishesAccountCreatedEvent()
  {
    var command = new CreateAccountCommand(
      Livemode: false,
      DisplayName: "Furever",
      ContactEmail: "furever@example.com",
      ContactPhone: null,
      AppliedConfigurations: [AppliedConfiguration.Customer],
      ConfigurationJson: null,
      IdentityJson: null,
      DefaultsJson: null,
      MetadataJson: null);

    await _sut.Handle(command, CancellationToken.None);

    await _mediator.Received(1).Publish(
      Arg.Any<Hex.Scaffold.Domain.AccountAggregate.Events.AccountCreatedEvent>(),
      Arg.Any<CancellationToken>());
  }
}
