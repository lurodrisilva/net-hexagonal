using System.Diagnostics;

namespace Hex.Scaffold.Domain.Common;

// Shared ActivitySource for Kafka producer + consumer instrumentation.
// Confluent.Kafka has no first-party OTel package, so producer and consumer
// adapters open spans manually using this source. Both adapters reference
// Domain (per the hex layering rules), so this is the only project where
// the source can live without an Inbound→Outbound or vice-versa dependency.
//
// The TracerProvider in the Api composition root subscribes to this source
// via .AddSource(KafkaTelemetry.SourceName) so the spans flow through the
// existing OTLP + Azure Monitor exporters and the Live Metrics QuickPulse
// channel picks them up too.
public static class KafkaTelemetry
{
  public const string SourceName = "Hex.Scaffold.Kafka";

  public static readonly ActivitySource Source = new(SourceName);
}
