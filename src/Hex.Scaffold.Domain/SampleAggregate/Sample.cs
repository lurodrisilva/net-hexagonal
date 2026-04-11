using Hex.Scaffold.Domain.SampleAggregate.Events;

namespace Hex.Scaffold.Domain.SampleAggregate;

public class Sample(SampleName name) : EntityBase<Sample, SampleId>, IAggregateRoot
{
  public SampleName Name { get; private set; } = name;
  public SampleStatus Status { get; private set; } = SampleStatus.NotSet;
  public string? Description { get; private set; }

  public Sample UpdateName(SampleName newName)
  {
    if (Name == newName) return this;
    Name = newName;
    RegisterDomainEvent(new SampleUpdatedEvent(this));
    return this;
  }

  public Sample UpdateDescription(string? description)
  {
    Description = description;
    return this;
  }

  public Sample Activate()
  {
    if (Status == SampleStatus.Active) return this;
    Status = SampleStatus.Active;
    RegisterDomainEvent(new SampleUpdatedEvent(this));
    return this;
  }

  public Sample Deactivate()
  {
    if (Status == SampleStatus.Inactive) return this;
    Status = SampleStatus.Inactive;
    RegisterDomainEvent(new SampleUpdatedEvent(this));
    return this;
  }
}
