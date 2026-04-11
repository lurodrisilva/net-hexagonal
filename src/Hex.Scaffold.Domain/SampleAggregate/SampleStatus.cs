namespace Hex.Scaffold.Domain.SampleAggregate;

public sealed class SampleStatus : SmartEnum<SampleStatus>
{
  public static readonly SampleStatus Active = new(nameof(Active), 1);
  public static readonly SampleStatus Inactive = new(nameof(Inactive), 2);
  public static readonly SampleStatus NotSet = new(nameof(NotSet), 3);

  private SampleStatus(string name, int value) : base(name, value) { }
}
