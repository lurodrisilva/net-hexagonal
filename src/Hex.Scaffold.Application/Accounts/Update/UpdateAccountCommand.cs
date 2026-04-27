using Hex.Scaffold.Domain.AccountAggregate;

namespace Hex.Scaffold.Application.Accounts.Update;

// Mirrors Stripe's partial-update semantics: every field is "Maybe" — the
// API layer decides which keys the caller actually sent and only those
// arrive populated. Aligns with the (HasValue, Value) tuple the aggregate
// expects in ApplyUpdate so omitted keys don't accidentally clear data.
public sealed record UpdateAccountCommand(
  AccountId Id,
  (bool HasValue, string? Value) DisplayName,
  (bool HasValue, string? Value) ContactEmail,
  (bool HasValue, string? Value) ContactPhone,
  (bool HasValue, IReadOnlyList<AppliedConfiguration>? Value) AppliedConfigurations,
  (bool HasValue, string? Value) ConfigurationJson,
  (bool HasValue, string? Value) IdentityJson,
  (bool HasValue, string? Value) DefaultsJson,
  (bool HasValue, string? Value) MetadataJson)
  : ICommand<Result<AccountDto>>;
