namespace Hex.Scaffold.Api.Options;

public sealed class KafkaOptions
{
  public string BootstrapServers { get; set; } = string.Empty;
  public string ConsumerGroupId { get; set; } = "hex-scaffold-group";
}
