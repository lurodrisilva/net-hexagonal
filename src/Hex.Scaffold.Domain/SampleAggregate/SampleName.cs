using Vogen;

namespace Hex.Scaffold.Domain.SampleAggregate;

[ValueObject<string>(conversions: Conversions.SystemTextJson)]
public partial struct SampleName
{
  public const int MaxLength = 200;

  private static Validation Validate(in string value) =>
    string.IsNullOrWhiteSpace(value)
      ? Validation.Invalid("SampleName cannot be empty")
      : value.Length > MaxLength
        ? Validation.Invalid($"SampleName cannot be longer than {MaxLength} characters")
        : Validation.Ok;
}
