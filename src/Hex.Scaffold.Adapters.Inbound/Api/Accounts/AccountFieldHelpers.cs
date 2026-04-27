using System.Text.Json;
using Hex.Scaffold.Domain.AccountAggregate;

namespace Hex.Scaffold.Adapters.Inbound.Api.Accounts;

// Stripe's PATCH semantics need three states per field: omitted (leave
// alone), explicit null (clear), explicit value (set). System.Text.Json's
// JsonElement carries that exactly: `Undefined` for omitted keys, `Null`
// for null literals, anything else for a value. The helpers below collapse
// the JsonElement back into the (HasValue, Value?) tuple shape the domain
// aggregate's ApplyUpdate accepts.
internal static class AccountFieldHelpers
{
  public static (bool HasValue, string? Value) ToMaybeString(this JsonElement el) =>
    el.ValueKind switch
    {
      JsonValueKind.Undefined => (false, null),
      JsonValueKind.Null      => (true, null),
      _                        => (true, el.GetString())
    };

  public static (bool HasValue, string? Value) ToMaybeRawJson(this JsonElement el) =>
    el.ValueKind switch
    {
      JsonValueKind.Undefined => (false, null),
      JsonValueKind.Null      => (true, null),
      _                        => (true, el.GetRawText())
    };

  // Materialize the wire-format string array into the SmartEnum set the
  // domain expects. Unknown values throw — matches Stripe's 400-on-unknown
  // behavior; FastEndpoints turns the exception into a problem detail
  // before it reaches the client.
  public static (bool HasValue, IReadOnlyList<AppliedConfiguration>? Value) ToMaybeAppliedConfigs(this JsonElement el)
  {
    if (el.ValueKind == JsonValueKind.Undefined) return (false, null);
    if (el.ValueKind == JsonValueKind.Null) return (true, null);
    if (el.ValueKind != JsonValueKind.Array)
      throw new ArgumentException("applied_configurations must be an array of strings.");

    var list = new List<AppliedConfiguration>();
    foreach (var item in el.EnumerateArray())
    {
      if (item.ValueKind != JsonValueKind.String)
        throw new ArgumentException("applied_configurations must be an array of strings.");
      var value = item.GetString();
      var matched = AppliedConfiguration.List.FirstOrDefault(c => c.Value == value)
        ?? throw new ArgumentException(
          $"applied_configurations[*] must be one of: customer, merchant, recipient (got '{value}').");
      list.Add(matched);
    }
    return (true, list);
  }

  // Used by the create endpoint to convert the (always-present, possibly
  // empty) array directly into the SmartEnum list — no Maybe wrapper.
  public static List<AppliedConfiguration> ToAppliedConfigs(IEnumerable<string>? raw)
  {
    if (raw is null) return [];
    var list = new List<AppliedConfiguration>();
    foreach (var value in raw)
    {
      var matched = AppliedConfiguration.List.FirstOrDefault(c => c.Value == value)
        ?? throw new ArgumentException(
          $"applied_configurations[*] must be one of: customer, merchant, recipient (got '{value}').");
      list.Add(matched);
    }
    return list;
  }
}
