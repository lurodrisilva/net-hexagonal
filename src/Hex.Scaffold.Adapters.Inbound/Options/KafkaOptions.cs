namespace Hex.Scaffold.Adapters.Inbound.Options;

public sealed class KafkaOptions
{
  public string BootstrapServers { get; set; } = string.Empty;
  public string ConsumerGroupId { get; set; } = "hex-scaffold-group";
  public string InboundTopic { get; set; } = "v2.core.accounts";
}
