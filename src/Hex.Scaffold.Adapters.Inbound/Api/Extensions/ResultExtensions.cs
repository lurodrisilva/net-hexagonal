using Ardalis.Result;
using Microsoft.AspNetCore.Http;

namespace Hex.Scaffold.Adapters.Inbound.Api.Extensions;

public static class ResultExtensions
{
  public static Results<Created<TResponse>, ValidationProblem, ProblemHttpResult>
    ToCreatedResult<TValue, TResponse>(
      this Result<TValue> result,
      Func<TValue, string> locationBuilder,
      Func<TValue, TResponse> responseBuilder)
  {
    if (result.IsSuccess)
      return TypedResults.Created(locationBuilder(result.Value), responseBuilder(result.Value));

    if (result.Status == ResultStatus.Invalid)
    {
      var errors = result.ValidationErrors
        .GroupBy(e => e.Identifier)
        .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
      return TypedResults.ValidationProblem(errors);
    }

    return TypedResults.Problem(result.Errors.FirstOrDefault() ?? "An error occurred.");
  }

  public static Results<Ok<TResponse>, NotFound, ProblemHttpResult>
    ToGetByIdResult<TValue, TResponse>(
      this Result<TValue> result,
      Func<TValue, TResponse> responseBuilder)
  {
    if (result.IsSuccess)
      return TypedResults.Ok(responseBuilder(result.Value));

    if (result.Status == ResultStatus.NotFound)
      return TypedResults.NotFound();

    return TypedResults.Problem(result.Errors.FirstOrDefault() ?? "An error occurred.");
  }

  public static Results<Ok<TResponse>, NotFound, ProblemHttpResult>
    ToUpdateResult<TValue, TResponse>(
      this Result<TValue> result,
      Func<TValue, TResponse> responseBuilder)
  {
    if (result.IsSuccess)
      return TypedResults.Ok(responseBuilder(result.Value));

    if (result.Status == ResultStatus.NotFound)
      return TypedResults.NotFound();

    return TypedResults.Problem(result.Errors.FirstOrDefault() ?? "An error occurred.");
  }

  public static Results<NoContent, NotFound, ProblemHttpResult>
    ToDeleteResult(this Result result)
  {
    if (result.IsSuccess)
      return TypedResults.NoContent();

    if (result.Status == ResultStatus.NotFound)
      return TypedResults.NotFound();

    return TypedResults.Problem(result.Errors.FirstOrDefault() ?? "An error occurred.");
  }

  public static Ok<TResponse> ToOkOnlyResult<TValue, TResponse>(
    this Result<TValue> result,
    Func<TValue, TResponse> responseBuilder) =>
    TypedResults.Ok(responseBuilder(result.Value));
}
