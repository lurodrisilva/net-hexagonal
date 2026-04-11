using Hex.Scaffold.Domain.SampleAggregate;

namespace Hex.Scaffold.Application.Samples.Update;

public record UpdateSampleCommand(SampleId Id, SampleName Name, string? Description)
  : ICommand<Result<SampleDto>>;
