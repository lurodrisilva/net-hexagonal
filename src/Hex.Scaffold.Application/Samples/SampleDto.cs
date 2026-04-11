using Hex.Scaffold.Domain.SampleAggregate;

namespace Hex.Scaffold.Application.Samples;

public record SampleDto(SampleId Id, SampleName Name, SampleStatus Status, string? Description);
