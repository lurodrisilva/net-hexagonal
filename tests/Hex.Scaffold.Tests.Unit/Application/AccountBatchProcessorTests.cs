using Hex.Scaffold.Application.Accounts.Batch;
using Hex.Scaffold.Domain.AccountAggregate;
using Hex.Scaffold.Domain.AccountAggregate.Events;

namespace Hex.Scaffold.Tests.Unit.Application;

[Trait("Category", "Unit")]
public class AccountBatchProcessorTests
{
  private readonly IRepository<Account> _repository = Substitute.For<IRepository<Account>>();
  private readonly IMediator _mediator = Substitute.For<IMediator>();
  private readonly AccountBatchProcessor _sut;

  public AccountBatchProcessorTests()
  {
    _sut = new AccountBatchProcessor(
      _repository,
      _mediator,
      Substitute.For<ILogger<AccountBatchProcessor>>());

    // Default: GetByIdsAsync returns empty so creates aren't filtered as
    // duplicates and updates/deletes for unknown ids are dropped.
    _repository.GetByIdsAsync<AccountId>(Arg.Any<IReadOnlyList<AccountId>>(), Arg.Any<CancellationToken>())
      .Returns(Task.FromResult<IReadOnlyList<Account>>([]));
  }

  // ---------------------------------------------------------------------------
  // Helpers
  // ---------------------------------------------------------------------------

  // 22 base32-ish chars after the acct_ prefix — matches the format
  // Account.NewId() produces, so AccountId.From validates cleanly.
  private static AccountId NewExternalId(string seed) =>
    AccountId.From(AccountId.Prefix + seed.PadRight(22, '0')[..22]);

  private static AccountInboundEvent CreateEvent(AccountId id, string? displayName = "Test") =>
    new(
      EventType: nameof(AccountCreatedEvent),
      PayloadJson: $$"""
      {
        "id": "{{id.Value}}",
        "livemode": false,
        "display_name": {{(displayName is null ? "null" : $"\"{displayName}\"")}}
      }
      """);

  private static AccountInboundEvent UpdateEvent(AccountId id, string newDisplayName) =>
    new(
      EventType: nameof(AccountUpdatedEvent),
      PayloadJson: $$"""
      {
        "id": "{{id.Value}}",
        "display_name": "{{newDisplayName}}"
      }
      """);

  private static AccountInboundEvent DeleteEvent(AccountId id) =>
    new(
      EventType: nameof(AccountDeletedEvent),
      PayloadJson: $$"""
      {"id": "{{id.Value}}"}
      """);

  private static Account ExistingAccount(AccountId id) =>
    Account.CreateWithExternalId(
      id: id,
      livemode: false,
      displayName: "Existing",
      contactEmail: null,
      contactPhone: null,
      appliedConfigurations: null,
      configurationJson: null,
      identityJson: null,
      defaultsJson: null,
      metadataJson: null);

  // ---------------------------------------------------------------------------
  // Tests
  // ---------------------------------------------------------------------------

  [Fact]
  public async Task ProcessAsync_EmptyInput_DoesNotCallRepository()
  {
    await _sut.ProcessAsync([], CancellationToken.None);

    await _repository.DidNotReceiveWithAnyArgs().GetByIdsAsync<AccountId>(default!, default);
    await _repository.DidNotReceiveWithAnyArgs().SaveBatchAsync(default!, default);
  }

  [Fact]
  public async Task ProcessAsync_ThreeCreates_FiresSingleSaveBatchAsyncWithThreeAddOps()
  {
    var events = new[]
    {
      CreateEvent(NewExternalId("create0001")),
      CreateEvent(NewExternalId("create0002")),
      CreateEvent(NewExternalId("create0003")),
    };

    IReadOnlyList<BatchOp<Account>>? captured = null;
    _repository.SaveBatchAsync(Arg.Any<IReadOnlyList<BatchOp<Account>>>(), Arg.Any<CancellationToken>())
      .Returns(call => { captured = call.Arg<IReadOnlyList<BatchOp<Account>>>(); return Task.CompletedTask; });

    await _sut.ProcessAsync(events, CancellationToken.None);

    await _repository.Received(1).SaveBatchAsync(Arg.Any<IReadOnlyList<BatchOp<Account>>>(), Arg.Any<CancellationToken>());
    captured.ShouldNotBeNull();
    captured!.Count.ShouldBe(3);
    captured.ShouldAllBe(op => op.Kind == BatchOpKind.Add);
  }

  [Fact]
  public async Task ProcessAsync_MixedCreateUpdateDelete_ProducesCorrectOpKinds()
  {
    var createId = NewExternalId("createMix1");
    var updateId = NewExternalId("updateMix1");
    var deleteId = NewExternalId("deleteMix1");

    // Update + Delete need their target rows to already exist or the ops
    // get dropped (mirrors REST handler semantics).
    _repository.GetByIdsAsync<AccountId>(Arg.Any<IReadOnlyList<AccountId>>(), Arg.Any<CancellationToken>())
      .Returns(Task.FromResult<IReadOnlyList<Account>>(
        [ExistingAccount(updateId), ExistingAccount(deleteId)]));

    var events = new[]
    {
      CreateEvent(createId),
      UpdateEvent(updateId, "Renamed"),
      DeleteEvent(deleteId),
    };

    IReadOnlyList<BatchOp<Account>>? captured = null;
    _repository.SaveBatchAsync(Arg.Any<IReadOnlyList<BatchOp<Account>>>(), Arg.Any<CancellationToken>())
      .Returns(call => { captured = call.Arg<IReadOnlyList<BatchOp<Account>>>(); return Task.CompletedTask; });

    await _sut.ProcessAsync(events, CancellationToken.None);

    await _repository.Received(1).SaveBatchAsync(Arg.Any<IReadOnlyList<BatchOp<Account>>>(), Arg.Any<CancellationToken>());
    captured.ShouldNotBeNull();
    captured!.Count.ShouldBe(3);
    captured.Count(op => op.Kind == BatchOpKind.Add).ShouldBe(1);
    captured.Count(op => op.Kind == BatchOpKind.Update).ShouldBe(1);
    captured.Count(op => op.Kind == BatchOpKind.Delete).ShouldBe(1);
  }

  [Fact]
  public async Task ProcessAsync_IdempotentCreate_SkipsAddOpWhenAggregateAlreadyExists()
  {
    var existingId = NewExternalId("dupcreate1");

    _repository.GetByIdsAsync<AccountId>(Arg.Any<IReadOnlyList<AccountId>>(), Arg.Any<CancellationToken>())
      .Returns(Task.FromResult<IReadOnlyList<Account>>([ExistingAccount(existingId)]));

    var events = new[] { CreateEvent(existingId) };

    await _sut.ProcessAsync(events, CancellationToken.None);

    // No write at all — single-item batch with the only op skipped means
    // ops.Count == 0 and the SaveBatchAsync call is short-circuited.
    await _repository.DidNotReceiveWithAnyArgs().SaveBatchAsync(default!, default);
  }

  [Fact]
  public async Task ProcessAsync_UpdateForUnknownId_SkipsOpWithoutError()
  {
    // GetByIdsAsync default returns empty — so the update target is unknown.
    var events = new[] { UpdateEvent(NewExternalId("ghostupd01"), "Whatever") };

    await _sut.ProcessAsync(events, CancellationToken.None);

    await _repository.DidNotReceiveWithAnyArgs().SaveBatchAsync(default!, default);
  }

  [Fact]
  public async Task ProcessAsync_DeleteForUnknownId_SkipsOpWithoutError()
  {
    // GetByIdsAsync default returns empty — so the delete target is unknown.
    var events = new[] { DeleteEvent(NewExternalId("ghostdel01")) };

    await _sut.ProcessAsync(events, CancellationToken.None);

    await _repository.DidNotReceiveWithAnyArgs().SaveBatchAsync(default!, default);
  }

  [Fact]
  public async Task ProcessAsync_PublishesAccountCreatedEventForCreates()
  {
    var events = new[]
    {
      CreateEvent(NewExternalId("evtcreate1")),
      CreateEvent(NewExternalId("evtcreate2")),
    };

    await _sut.ProcessAsync(events, CancellationToken.None);

    await _mediator.Received(2).Publish(
      Arg.Any<AccountCreatedEvent>(),
      Arg.Any<CancellationToken>());
  }
}
