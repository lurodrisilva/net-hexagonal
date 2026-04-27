using Vogen;

namespace Hex.Scaffold.Domain.AccountAggregate;

// Stripe v2 Account IDs are opaque strings prefixed with "acct_". Mirroring
// the wire format means a string-typed Vogen value object — no Hi-Lo
// sequence, no integer key, no ISampleIdGenerator port. Generation is a
// pure-function helper on Account itself (Account.NewId) that hands a fully-
// initialized AccountId to the aggregate constructor before EF's
// IdentityMap.Add ever sees it (the same lesson PR #20 paid for).
[ValueObject<string>(conversions: Conversions.SystemTextJson)]
public readonly partial struct AccountId
{
  public const string Prefix = "acct_";

  private static Validation Validate(string value)
    => !string.IsNullOrWhiteSpace(value) && value.StartsWith(Prefix, StringComparison.Ordinal)
      ? Validation.Ok
      : Validation.Invalid($"AccountId must start with '{Prefix}'.");
}
