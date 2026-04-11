namespace Hex.Scaffold.Domain.Ports.Outbound;

public sealed class SampleReadModel
{
  public int SampleId { get; init; }
  public string Name { get; init; } = string.Empty;
  public string Status { get; init; } = string.Empty;
  public string? Description { get; init; }
  public DateTime LastUpdated { get; init; }
}
