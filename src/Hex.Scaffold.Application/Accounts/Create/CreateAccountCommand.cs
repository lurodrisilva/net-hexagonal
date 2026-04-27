using Hex.Scaffold.Domain.AccountAggregate;

namespace Hex.Scaffold.Application.Accounts.Create;

// Inbound DTO already split out by the API layer; the command carries a
// pre-validated set of value objects + the raw JSON blobs for the nested
// fields the domain doesn't shape-check.
public sealed record CreateAccountCommand(
  bool Livemode,
  string? DisplayName,
  string? ContactEmail,
  string? ContactPhone,
  IReadOnlyList<AppliedConfiguration> AppliedConfigurations,
  string? ConfigurationJson,
  string? IdentityJson,
  string? DefaultsJson,
  string? MetadataJson)
  : ICommand<Result<AccountDto>>;
