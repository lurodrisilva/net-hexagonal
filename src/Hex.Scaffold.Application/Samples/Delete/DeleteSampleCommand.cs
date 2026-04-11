using Hex.Scaffold.Domain.SampleAggregate;

namespace Hex.Scaffold.Application.Samples.Delete;

public record DeleteSampleCommand(SampleId SampleId) : ICommand<Result>;
