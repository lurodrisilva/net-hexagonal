using System.Security.Cryptography;
using Hex.Scaffold.Domain.AccountAggregate.Events;

namespace Hex.Scaffold.Domain.AccountAggregate;

// Aggregate root for `v2.core.account` — see the Stripe v2 Account docs for
// the wire shape. Top-level scalars are persisted as proper columns so the
// list endpoint can paginate by `created` and the merchant-of-record query
// paths stay indexable. The deeply nested structures (configuration,
// identity, defaults, requirements, future_requirements, metadata) live as
// raw JSON strings round-tripped through Postgres jsonb columns — the API
// boundary deserializes them to JsonElement on the way out and parses
// caller-provided JsonElement back into a string on the way in. This keeps
// the wire format byte-for-byte faithful to Stripe without exploding the
// domain into ~80 nested record types whose only job is to mirror Stripe's
// enum surface (50+ payment-method capabilities, 200+ tax-id types, every
// ISO-3166 country, etc.). The trade-off is that the domain can't enforce
// shape on the nested blobs — validation lives in handlers / FluentValidation.
public class Account : EntityBase<Account, AccountId>, IAggregateRoot
{
  public const string ObjectKind = "v2.core.account";

  // EF Core parameterized-constructor binding requires this ctor for
  // rehydration; the lesson from PR #20 still applies — Id must be a fully-
  // initialized AccountId before the entry hits ChangeTracker.
  private Account(
    AccountId id,
    bool livemode,
    DateTime created,
    bool closed,
    string? displayName,
    string? contactEmail,
    string? contactPhone,
    string? dashboard,
    List<string> appliedConfigurations,
    string? configurationJson,
    string? identityJson,
    string? defaultsJson,
    string? requirementsJson,
    string? futureRequirementsJson,
    string? metadataJson)
  {
    Id = id;
    Livemode = livemode;
    Created = created;
    Closed = closed;
    DisplayName = displayName;
    ContactEmail = contactEmail;
    ContactPhone = contactPhone;
    Dashboard = dashboard;
    AppliedConfigurations = appliedConfigurations;
    ConfigurationJson = configurationJson;
    IdentityJson = identityJson;
    DefaultsJson = defaultsJson;
    RequirementsJson = requirementsJson;
    FutureRequirementsJson = futureRequirementsJson;
    MetadataJson = metadataJson;
  }

  public bool Livemode { get; private set; }
  public DateTime Created { get; private set; }
  public bool Closed { get; private set; }
  public string? DisplayName { get; private set; }
  public string? ContactEmail { get; private set; }
  public string? ContactPhone { get; private set; }

  // Stripe enum values: full | express | none (nullable when no config
  // implies a hosted dashboard). Stored as string to avoid yet another
  // SmartEnum for a value that's purely informational on the wire.
  public string? Dashboard { get; private set; }

  // Persisted as a Postgres text[] so callers can do
  // `WHERE 'merchant' = ANY(applied_configurations)` cheaply.
  public List<string> AppliedConfigurations { get; private set; } = [];

  // Nested-object payloads — raw canonical JSON strings persisted to jsonb.
  // Intentionally not strongly typed in the domain; see class doc.
  public string? ConfigurationJson { get; private set; }
  public string? IdentityJson { get; private set; }
  public string? DefaultsJson { get; private set; }
  public string? RequirementsJson { get; private set; }
  public string? FutureRequirementsJson { get; private set; }
  public string? MetadataJson { get; private set; }

  // Factory used by CreateAccountHandler. The aggregate generates its own
  // ID at construction time so persistence has nothing to do (no Hi-Lo, no
  // ISampleIdGenerator port).
  public static Account Create(
    bool livemode,
    string? displayName,
    string? contactEmail,
    string? contactPhone,
    IEnumerable<AppliedConfiguration>? appliedConfigurations,
    string? configurationJson,
    string? identityJson,
    string? defaultsJson,
    string? metadataJson,
    DateTime? createdUtc = null)
  {
    var account = new Account(
      id: NewId(),
      livemode: livemode,
      created: createdUtc ?? DateTime.UtcNow,
      closed: false,
      displayName: displayName,
      contactEmail: contactEmail,
      contactPhone: contactPhone,
      dashboard: DeriveDashboard(appliedConfigurations),
      appliedConfigurations: (appliedConfigurations ?? []).Select(c => c.Value).Distinct().ToList(),
      configurationJson: configurationJson,
      identityJson: identityJson,
      defaultsJson: defaultsJson,
      requirementsJson: null,
      futureRequirementsJson: null,
      metadataJson: metadataJson);

    account.RegisterDomainEvent(new AccountCreatedEvent(account));
    return account;
  }

  // Stripe's update endpoint accepts a partial mutation — every field is
  // optional, omission means "leave alone". We model that with `Maybe<T>`-
  // shaped (HasValue, Value) tuples so callers can pass null-as-explicit
  // (e.g. clearing display_name) without colliding with omitted fields.
  // Single mutator keeps the AccountUpdatedEvent emission to one site.
  public Account ApplyUpdate(
    (bool HasValue, string? Value) displayName,
    (bool HasValue, string? Value) contactEmail,
    (bool HasValue, string? Value) contactPhone,
    (bool HasValue, IReadOnlyList<AppliedConfiguration>? Value) appliedConfigurations,
    (bool HasValue, string? Value) configurationJson,
    (bool HasValue, string? Value) identityJson,
    (bool HasValue, string? Value) defaultsJson,
    (bool HasValue, string? Value) metadataJson)
  {
    var changed = false;

    if (displayName.HasValue && DisplayName != displayName.Value)
    {
      DisplayName = displayName.Value;
      changed = true;
    }
    if (contactEmail.HasValue && ContactEmail != contactEmail.Value)
    {
      ContactEmail = contactEmail.Value;
      changed = true;
    }
    if (contactPhone.HasValue && ContactPhone != contactPhone.Value)
    {
      ContactPhone = contactPhone.Value;
      changed = true;
    }
    if (appliedConfigurations.HasValue)
    {
      var next = (appliedConfigurations.Value ?? []).Select(c => c.Value).Distinct().ToList();
      if (!next.SequenceEqual(AppliedConfigurations))
      {
        AppliedConfigurations = next;
        Dashboard = DeriveDashboard(appliedConfigurations.Value);
        changed = true;
      }
    }
    if (configurationJson.HasValue && ConfigurationJson != configurationJson.Value)
    {
      ConfigurationJson = configurationJson.Value;
      changed = true;
    }
    if (identityJson.HasValue && IdentityJson != identityJson.Value)
    {
      IdentityJson = identityJson.Value;
      changed = true;
    }
    if (defaultsJson.HasValue && DefaultsJson != defaultsJson.Value)
    {
      DefaultsJson = defaultsJson.Value;
      changed = true;
    }
    if (metadataJson.HasValue && MetadataJson != metadataJson.Value)
    {
      MetadataJson = metadataJson.Value;
      changed = true;
    }

    if (changed) RegisterDomainEvent(new AccountUpdatedEvent(this));
    return this;
  }

  // Account.Close is the v2 replacement for "delete". Out of scope for this
  // PR — only the four endpoints requested are exposed — but kept here as
  // a state transition so the aggregate stays consistent if a future
  // endpoint surfaces it.
  public Account Close()
  {
    if (Closed) return this;
    Closed = true;
    RegisterDomainEvent(new AccountUpdatedEvent(this));
    return this;
  }

  // Stripe's portal renders different dashboards depending on which configs
  // are applied. Reproducing the derivation here so retrieve / list match
  // the example payload's `dashboard: "full"` for a customer+merchant
  // account. Recipient-only → express. No configs → none.
  private static string DeriveDashboard(IEnumerable<AppliedConfiguration>? configs)
  {
    if (configs is null) return "none";
    var set = configs.Select(c => c.Value).ToHashSet();
    if (set.Contains(AppliedConfiguration.Merchant.Value)) return "full";
    if (set.Contains(AppliedConfiguration.Customer.Value)) return "full";
    if (set.Contains(AppliedConfiguration.Recipient.Value)) return "express";
    return "none";
  }

  // 22 base32 chars after the "acct_" prefix, sourced from
  // RandomNumberGenerator. Avoids GUID's hyphens and case sensitivity, and
  // keeps the surface area to the alphabet Stripe actually uses.
  private static AccountId NewId()
  {
    const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
    Span<byte> bytes = stackalloc byte[22];
    RandomNumberGenerator.Fill(bytes);
    Span<char> chars = stackalloc char[22];
    for (var i = 0; i < bytes.Length; i++)
    {
      chars[i] = alphabet[bytes[i] % alphabet.Length];
    }
    return AccountId.From(AccountId.Prefix + new string(chars));
  }
}
