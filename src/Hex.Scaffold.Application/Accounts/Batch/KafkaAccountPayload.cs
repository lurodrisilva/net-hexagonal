using System.Text.Json;

namespace Hex.Scaffold.Application.Accounts.Batch;

// DTO mirroring the snake_case payload the k6 producer emits and the
// outbound producer's serialised AccountCreatedEvent/AccountUpdatedEvent
// shape. Every partial-update-capable field is JsonElement so
// (Undefined = absent, Null = explicit clear, value = set) survives the
// REST-equivalent ApplyUpdate path. Plain Id / Livemode are scalars that
// don't participate in partial-update semantics.
//
// Lives in the Application layer (not the inbound adapter) because the
// batch processor owns parsing — the consumer only delivers raw JSON.
internal sealed class KafkaAccountPayload
{
  public string? Id { get; set; }
  public bool? Livemode { get; set; }
  public JsonElement DisplayName { get; set; }
  public JsonElement ContactEmail { get; set; }
  public JsonElement ContactPhone { get; set; }
  public JsonElement AppliedConfigurations { get; set; }
  public JsonElement Configuration { get; set; }
  public JsonElement Identity { get; set; }
  public JsonElement Defaults { get; set; }
  public JsonElement Metadata { get; set; }
}

internal static class JsonElementExtensions
{
  public static string? GetStringOrNull(this JsonElement el) =>
    el.ValueKind == JsonValueKind.String ? el.GetString() : null;

  public static string? GetRawTextOrNull(this JsonElement el) =>
    el.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ? null : el.GetRawText();

  public static List<string>? AsStringList(this JsonElement el)
  {
    if (el.ValueKind != JsonValueKind.Array) return null;
    var list = new List<string>();
    foreach (var item in el.EnumerateArray())
    {
      if (item.ValueKind == JsonValueKind.String && item.GetString() is { } s) list.Add(s);
    }
    return list;
  }

  // Maybe-tuple helpers for partial-update semantics. Mirrors the inbound
  // adapter's AccountFieldHelpers but lives here because the batch processor
  // is what now owns parsing for the Kafka path.
  public static (bool HasValue, string? Value) ToMaybeString(this JsonElement el) =>
    el.ValueKind switch
    {
      JsonValueKind.Undefined => (false, null),
      JsonValueKind.Null => (true, null),
      _ => (true, el.GetString())
    };

  public static (bool HasValue, string? Value) ToMaybeRawJson(this JsonElement el) =>
    el.ValueKind switch
    {
      JsonValueKind.Undefined => (false, null),
      JsonValueKind.Null => (true, null),
      _ => (true, el.GetRawText())
    };
}
