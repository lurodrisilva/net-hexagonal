namespace Hex.Scaffold.Domain.Common;

public enum ResultStatus
{
  Ok,
  NotFound,
  Invalid,
  Error
}

public record ValidationError(string Identifier, string ErrorMessage);

public class Result
{
  public ResultStatus Status { get; private init; } = ResultStatus.Ok;
  public bool IsSuccess => Status == ResultStatus.Ok;
  public List<string> Errors { get; private init; } = [];
  public List<ValidationError> ValidationErrors { get; private init; } = [];

  public static Result Success() => new();
  public static Result NotFound() => new() { Status = ResultStatus.NotFound };
  public static Result Error(string error) => new() { Status = ResultStatus.Error, Errors = [error] };
  public static Result Invalid(params ValidationError[] errors) =>
    new() { Status = ResultStatus.Invalid, ValidationErrors = [.. errors] };

  public static Result<T> Success<T>(T value) => Result<T>.Success(value);
}

public class Result<T>
{
  public T Value { get; private init; } = default!;
  public ResultStatus Status { get; private init; } = ResultStatus.Ok;
  public bool IsSuccess => Status == ResultStatus.Ok;
  public List<string> Errors { get; private init; } = [];
  public List<ValidationError> ValidationErrors { get; private init; } = [];

  public static Result<T> Success(T value) => new() { Value = value };
  public static Result<T> NotFound() => new() { Status = ResultStatus.NotFound };
  public static Result<T> Error(string error) => new() { Status = ResultStatus.Error, Errors = [error] };
  public static Result<T> Invalid(params ValidationError[] errors) =>
    new() { Status = ResultStatus.Invalid, ValidationErrors = [.. errors] };

  public static implicit operator Result<T>(T value) => Success(value);
}
