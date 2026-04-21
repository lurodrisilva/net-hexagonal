namespace Hex.Scaffold.Api.Options;

/// <summary>
/// Runtime selector for which hexagonal adapters are wired up. Populated from
/// the <c>Features</c> section of configuration (typically supplied by the
/// Helm chart's ConfigMap via environment variables).
///
/// Invariants (enforced by <see cref="Validate"/>):
///   * Exactly one inbound adapter (REST or Kafka consumer).
///   * Exactly one outbound adapter (REST-over-HTTP or Kafka producer).
///   * Exactly one primary persistence store (PostgreSQL or MongoDB).
///   * Redis (cache) is optional and only valid when PostgreSQL is primary.
/// </summary>
public sealed class FeaturesOptions
{
  public const string SectionName = "Features";

  public string InboundAdapter { get; set; } = InboundKinds.Rest;
  public string OutboundAdapter { get; set; } = OutboundKinds.Rest;
  public string Persistence { get; set; } = PersistenceKinds.Postgres;
  public bool UseRedis { get; set; } = false;

  public static class InboundKinds
  {
    public const string Rest = "rest";
    public const string Kafka = "kafka";
  }

  public static class OutboundKinds
  {
    public const string Rest = "rest";
    public const string Kafka = "kafka";
  }

  public static class PersistenceKinds
  {
    public const string Postgres = "postgres";
    public const string Mongo = "mongo";
  }

  public bool InboundKafkaEnabled => string.Equals(InboundAdapter, InboundKinds.Kafka, StringComparison.OrdinalIgnoreCase);
  public bool InboundRestEnabled => string.Equals(InboundAdapter, InboundKinds.Rest, StringComparison.OrdinalIgnoreCase);
  public bool OutboundKafkaEnabled => string.Equals(OutboundAdapter, OutboundKinds.Kafka, StringComparison.OrdinalIgnoreCase);
  public bool OutboundRestEnabled => string.Equals(OutboundAdapter, OutboundKinds.Rest, StringComparison.OrdinalIgnoreCase);
  public bool PostgresEnabled => string.Equals(Persistence, PersistenceKinds.Postgres, StringComparison.OrdinalIgnoreCase);
  public bool MongoEnabled => string.Equals(Persistence, PersistenceKinds.Mongo, StringComparison.OrdinalIgnoreCase);

  public void Validate()
  {
    if (!InboundRestEnabled && !InboundKafkaEnabled)
      throw new InvalidOperationException($"Features:InboundAdapter must be '{InboundKinds.Rest}' or '{InboundKinds.Kafka}', got '{InboundAdapter}'.");
    if (!OutboundRestEnabled && !OutboundKafkaEnabled)
      throw new InvalidOperationException($"Features:OutboundAdapter must be '{OutboundKinds.Rest}' or '{OutboundKinds.Kafka}', got '{OutboundAdapter}'.");
    if (!PostgresEnabled && !MongoEnabled)
      throw new InvalidOperationException($"Features:Persistence must be '{PersistenceKinds.Postgres}' or '{PersistenceKinds.Mongo}', got '{Persistence}'.");
    if (UseRedis && !PostgresEnabled)
      throw new InvalidOperationException("Features:UseRedis=true requires Features:Persistence='postgres' (Redis cache is only paired with PostgreSQL).");
  }
}
