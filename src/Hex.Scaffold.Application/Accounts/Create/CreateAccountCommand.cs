using Hex.Scaffold.Domain.AccountAggregate;

namespace Hex.Scaffold.Application.Accounts.Create;

// Inbound DTO already split out by the API layer; the command carries a
// pre-validated set of value objects + the raw JSON blobs for the nested
// fields the domain doesn't shape-check.
//
// Id is optional and trails the rest of the parameters so the REST endpoint
// (which never supplies one — server-generated id) keeps compiling unchanged.
// When supplied (Kafka inbound load-test path), the handler routes through
// Account.CreateWithExternalId and applies an idempotency guard for at-least-
// once event delivery.
public sealed record CreateAccountCommand(
  bool Livemode,
  string? DisplayName,
  string? ContactEmail,
  string? ContactPhone,
  IReadOnlyList<AppliedConfiguration> AppliedConfigurations,
  string? ConfigurationJson,
  string? IdentityJson,
  string? DefaultsJson,
  string? MetadataJson,
  AccountId? Id = null)
  : ICommand<Result<AccountDto>>;
