namespace Hex.Scaffold.Domain.SampleAggregate.Specifications;

public sealed class SampleByIdSpec : Specification<Sample>
{
  public SampleByIdSpec(SampleId id) =>
    Query.Where(s => s.Id == id);
}
