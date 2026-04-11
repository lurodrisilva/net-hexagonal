using Hex.Scaffold.Domain.SampleAggregate;
using Hex.Scaffold.Domain.SampleAggregate.Events;

namespace Hex.Scaffold.Tests.Unit.Domain;

[Trait("Category", "Unit")]
public class SampleAggregateTests
{
  [Fact]
  public void CreateSample_WithValidName_SetsPropertiesCorrectly()
  {
    var name = SampleName.From("Test Sample");
    var sample = new Sample(name);

    sample.Name.ShouldBe(name);
    sample.Status.ShouldBe(SampleStatus.NotSet);
    sample.Description.ShouldBeNull();
    sample.DomainEvents.ShouldBeEmpty();
  }

  [Fact]
  public void UpdateName_WithDifferentName_RegistersUpdatedEvent()
  {
    var sample = new Sample(SampleName.From("Original"));
    var newName = SampleName.From("Updated");

    sample.UpdateName(newName);

    sample.Name.ShouldBe(newName);
    sample.DomainEvents.Count.ShouldBe(1);
    sample.DomainEvents.First().ShouldBeOfType<SampleUpdatedEvent>();
  }

  [Fact]
  public void UpdateName_WithSameName_DoesNotRegisterEvent()
  {
    var name = SampleName.From("Same Name");
    var sample = new Sample(name);

    sample.UpdateName(name);

    sample.DomainEvents.ShouldBeEmpty();
  }

  [Fact]
  public void Activate_WhenNotSet_SetsActiveAndRegistersEvent()
  {
    var sample = new Sample(SampleName.From("Test"));

    sample.Activate();

    sample.Status.ShouldBe(SampleStatus.Active);
    sample.DomainEvents.Count.ShouldBe(1);
    sample.DomainEvents.First().ShouldBeOfType<SampleUpdatedEvent>();
  }

  [Fact]
  public void Deactivate_WhenActive_SetsInactiveAndRegistersEvent()
  {
    var sample = new Sample(SampleName.From("Test"));
    sample.Activate(); // registers event 1

    sample.Deactivate(); // registers event 2

    sample.Status.ShouldBe(SampleStatus.Inactive);
    // Both Activate and Deactivate register SampleUpdatedEvent
    sample.DomainEvents.Count.ShouldBe(2);
    sample.DomainEvents.All(e => e is SampleUpdatedEvent).ShouldBeTrue();
  }
}
