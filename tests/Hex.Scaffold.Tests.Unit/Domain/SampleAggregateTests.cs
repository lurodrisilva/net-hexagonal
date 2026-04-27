using Hex.Scaffold.Domain.SampleAggregate;
using Hex.Scaffold.Domain.SampleAggregate.Events;

namespace Hex.Scaffold.Tests.Unit.Domain;

[Trait("Category", "Unit")]
public class SampleAggregateTests
{
  private static Sample NewSample(string name) =>
    new(SampleId.From(1), SampleName.From(name));

  [Fact]
  public void CreateSample_WithValidName_SetsPropertiesCorrectly()
  {
    var id = SampleId.From(99);
    var name = SampleName.From("Test Sample");
    var sample = new Sample(id, name);

    sample.Id.ShouldBe(id);
    sample.Name.ShouldBe(name);
    sample.Status.ShouldBe(SampleStatus.NotSet);
    sample.Description.ShouldBeNull();
    sample.DomainEvents.ShouldBeEmpty();
  }

  [Fact]
  public void UpdateName_WithDifferentName_RegistersUpdatedEvent()
  {
    var sample = NewSample("Original");
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
    var sample = new Sample(SampleId.From(1), name);

    sample.UpdateName(name);

    sample.DomainEvents.ShouldBeEmpty();
  }

  [Fact]
  public void Activate_WhenNotSet_SetsActiveAndRegistersEvent()
  {
    var sample = NewSample("Test");

    sample.Activate();

    sample.Status.ShouldBe(SampleStatus.Active);
    sample.DomainEvents.Count.ShouldBe(1);
    sample.DomainEvents.First().ShouldBeOfType<SampleUpdatedEvent>();
  }

  [Fact]
  public void Deactivate_WhenActive_SetsInactiveAndRegistersEvent()
  {
    var sample = NewSample("Test");
    sample.Activate(); // registers event 1

    sample.Deactivate(); // registers event 2

    sample.Status.ShouldBe(SampleStatus.Inactive);
    // Both Activate and Deactivate register SampleUpdatedEvent
    sample.DomainEvents.Count.ShouldBe(2);
    sample.DomainEvents.All(e => e is SampleUpdatedEvent).ShouldBeTrue();
  }
}
