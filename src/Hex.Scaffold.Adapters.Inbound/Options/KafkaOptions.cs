namespace Hex.Scaffold.Adapters.Inbound.Options;

public sealed class KafkaOptions
{
  public string BootstrapServers { get; set; } = string.Empty;
  public string ConsumerGroupId { get; set; } = "hex-scaffold-group";
  public string InboundTopic { get; set; } = "v2.core.accounts";

  // Per-tier loadtest knob (plan §5.2). The name is a leaky abstraction —
  // it traces to Java consumer's `max.poll.records` but Confluent.Kafka .NET
  // has no direct equivalent: Consume() returns ONE record per call by
  // design, so this knob does NOT cap handler-batch size the way Java does.
  // Wired to librdkafka's `queued.min.messages` (ConsumerConfig.QueuedMinMessages),
  // which is a *prefetch floor* — keep at least N records buffered per
  // partition before going back to the broker. Effect on the loadtest:
  // smooths bursty broker fetches, raises memory ceiling per consumer,
  // amortises broker round-trip cost at high throughput. Has NO effect on
  // handler concurrency or commit batching. If a follow-up PR wires batched
  // commits or batched handlers, this knob will need to be re-thought
  // (likely renamed to QueuedMinMessages and a separate batch-size knob added).
  public int MaxPollRecords { get; set; } = 500;

  // Minimum bytes the broker will accumulate before responding to a fetch
  // request (Confluent.Kafka ConsumerConfig.FetchMinBytes). Default 1 matches
  // the library default — fetch returns immediately. Per-tier loadtest
  // overlays raise this to amortise broker round-trip cost at high throughput.
  public int FetchMinBytes { get; set; } = 1;
}
