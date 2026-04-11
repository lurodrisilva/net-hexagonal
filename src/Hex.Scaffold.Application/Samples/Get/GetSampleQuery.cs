using Hex.Scaffold.Domain.SampleAggregate;

namespace Hex.Scaffold.Application.Samples.Get;

public record GetSampleQuery(SampleId SampleId) : IQuery<Result<SampleDto>>;
