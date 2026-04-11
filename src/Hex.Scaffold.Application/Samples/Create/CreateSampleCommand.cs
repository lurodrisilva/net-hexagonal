using Hex.Scaffold.Domain.SampleAggregate;

namespace Hex.Scaffold.Application.Samples.Create;

public record CreateSampleCommand(SampleName Name, string? Description)
  : ICommand<Result<SampleId>>;
