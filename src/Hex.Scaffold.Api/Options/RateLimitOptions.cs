namespace Hex.Scaffold.Api.Options;

// Bound to the "RateLimit" configuration section. Defaults match the
// hardcoded values shipped before the section existed (PermitLimit=100,
// Window=60s, QueueLimit=0) so existing deployments remain unchanged
// after upgrade. The Helm chart surfaces these via values.yaml so a
// load-test profile can override the cap without touching code.
public sealed class RateLimitOptions
{
  public const string SectionName = "RateLimit";

  public int PermitLimit { get; set; } = 100;
  public int WindowSeconds { get; set; } = 60;
  public int QueueLimit { get; set; } = 0;
}
