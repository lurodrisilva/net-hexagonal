namespace Hex.Scaffold.Domain.AccountAggregate;

// Stripe's `applied_configurations` is the per-Account toggle for which of
// the three configurations (customer / merchant / recipient) are in effect.
// Modeled here as a closed value type with a finite set of singletons —
// not the scaffold's int-keyed SmartEnum<T>, because Stripe's wire values
// are strings ("customer" etc.) and we want exact-match equality semantics
// without the int-mapping ceremony.
public sealed class AppliedConfiguration : IEquatable<AppliedConfiguration>
{
  public static readonly AppliedConfiguration Customer  = new("customer");
  public static readonly AppliedConfiguration Merchant  = new("merchant");
  public static readonly AppliedConfiguration Recipient = new("recipient");

  // List exposed as a static for callers that need to iterate (e.g. the
  // inbound API helper translating wire strings into instances).
  public static IReadOnlyList<AppliedConfiguration> List { get; } =
    [Customer, Merchant, Recipient];

  public string Value { get; }

  private AppliedConfiguration(string value) => Value = value;

  public override string ToString() => Value;

  public bool Equals(AppliedConfiguration? other) => other is not null && Value == other.Value;
  public override bool Equals(object? obj) => Equals(obj as AppliedConfiguration);
  public override int GetHashCode() => Value.GetHashCode(StringComparison.Ordinal);
}
