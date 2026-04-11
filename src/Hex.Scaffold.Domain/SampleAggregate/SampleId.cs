using Vogen;

namespace Hex.Scaffold.Domain.SampleAggregate;

[ValueObject<int>]
public readonly partial struct SampleId
{
  private static Validation Validate(int value)
    => value > 0 ? Validation.Ok : Validation.Invalid("SampleId must be positive.");
}
