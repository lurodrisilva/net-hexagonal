using System.Text.Json;
using Hex.Scaffold.Domain.AccountAggregate;

namespace Hex.Scaffold.Application.Accounts;

// Stripe v2 Account wire shape. Top-level scalars come from the aggregate's
// proper columns; nested objects are exposed as JsonElement so the payload
// passes through opaque to whatever the handler persisted. Naming is
// PascalCase here — System.Text.Json's snake_case_lower policy (configured
// at composition root) renders these as `applied_configurations`,
// `contact_email`, etc. on the wire.
public sealed record AccountDto(
  string Id,
  string Object,
  bool Livemode,
  DateTime Created,
  bool? Closed,
  string? DisplayName,
  string? ContactEmail,
  string? ContactPhone,
  string? Dashboard,
  IReadOnlyList<string> AppliedConfigurations,
  JsonElement? Configuration,
  JsonElement? Identity,
  JsonElement? Defaults,
  JsonElement? Requirements,
  JsonElement? FutureRequirements,
  JsonElement? Metadata)
{
  public static AccountDto FromAggregate(Account a)
  {
    return new AccountDto(
      Id: a.Id.Value,
      Object: Account.ObjectKind,
      Livemode: a.Livemode,
      Created: a.Created,
      Closed: a.Closed ? true : null,
      DisplayName: a.DisplayName,
      ContactEmail: a.ContactEmail,
      ContactPhone: a.ContactPhone,
      Dashboard: a.Dashboard,
      AppliedConfigurations: a.AppliedConfigurations,
      Configuration: ParseOrNull(a.ConfigurationJson),
      Identity: ParseOrNull(a.IdentityJson),
      Defaults: ParseOrNull(a.DefaultsJson),
      Requirements: ParseOrNull(a.RequirementsJson),
      FutureRequirements: ParseOrNull(a.FutureRequirementsJson),
      Metadata: ParseOrNull(a.MetadataJson));
  }

  // Parse-on-output: aggregate stores the raw JSON; the API layer hands
  // back a JsonElement so System.Text.Json serializes it verbatim without
  // re-parsing into a typed graph. Returns null when the column is null
  // OR contains the JSON literal "null" — both should serialize as null.
  private static JsonElement? ParseOrNull(string? json)
  {
    if (string.IsNullOrWhiteSpace(json)) return null;
    using var doc = JsonDocument.Parse(json);
    if (doc.RootElement.ValueKind == JsonValueKind.Null) return null;
    return doc.RootElement.Clone();
  }
}

// Stripe-style cursor list envelope: { object: "list", data: [...], has_more }.
// The total-count and page fields the existing PagedResult<T> exposes are
// intentionally absent — Stripe doesn't return them, and computing them
// against a large accounts table would force a full COUNT every page.
public sealed record AccountListResult(
  string Object,
  IReadOnlyList<AccountDto> Data,
  bool HasMore)
{
  public static AccountListResult Empty { get; } = new("list", [], false);
  public static AccountListResult Wrap(IReadOnlyList<AccountDto> data, bool hasMore) =>
    new("list", data, hasMore);
}
