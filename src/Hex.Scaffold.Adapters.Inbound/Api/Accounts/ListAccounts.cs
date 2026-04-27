using Hex.Scaffold.Application.Accounts;
using Hex.Scaffold.Application.Accounts.List;

namespace Hex.Scaffold.Adapters.Inbound.Api.Accounts;

// GET /v2/core/accounts — Stripe-style cursor list. Returns
// {object: "list", data: [...], has_more: bool}. Query params:
//   * limit          1..100 (default 10)
//   * starting_after acct_… cursor for the next page (forward)
//   * ending_before  acct_… cursor for the previous page (backward)
public class ListAccounts(IMediator mediator)
  : Endpoint<ListAccountsRequest, Results<Ok<AccountListResult>, ProblemHttpResult>>
{
  public override void Configure()
  {
    Get("/v2/core/accounts");
    AllowAnonymous();
    Summary(s =>
    {
      s.Summary = "List Accounts";
      s.Description = "Reproduces Stripe v2 GET /v2/core/accounts (cursor-paginated).";
      s.Responses[200] = "Account list";
      s.Responses[400] = "Invalid cursor combination";
    });
    Tags("Accounts");
  }

  public override async Task<Results<Ok<AccountListResult>, ProblemHttpResult>>
    ExecuteAsync(ListAccountsRequest request, CancellationToken ct)
  {
    var result = await mediator.Send(new ListAccountsQuery(
      Limit: request.Limit ?? ListAccountsHandler.DefaultLimit,
      StartingAfter: request.StartingAfter,
      EndingBefore: request.EndingBefore), ct);

    if (result.IsSuccess) return TypedResults.Ok(result.Value);
    return TypedResults.Problem(result.Errors.FirstOrDefault() ?? "An error occurred.");
  }
}

public class ListAccountsRequest
{
  public int? Limit { get; set; }
  public string? StartingAfter { get; set; }
  public string? EndingBefore { get; set; }
}

public class ListAccountsValidator : Validator<ListAccountsRequest>
{
  public ListAccountsValidator()
  {
    RuleFor(x => x.Limit)
      .InclusiveBetween(1, 100)
      .When(x => x.Limit.HasValue);
    RuleFor(x => x)
      .Must(x => !(x.StartingAfter is not null && x.EndingBefore is not null))
      .WithMessage("starting_after and ending_before are mutually exclusive.");
    RuleFor(x => x.StartingAfter)
      .Must(id => id is null || id.StartsWith("acct_", StringComparison.Ordinal))
      .WithMessage("starting_after must be an Account id starting with 'acct_'.");
    RuleFor(x => x.EndingBefore)
      .Must(id => id is null || id.StartsWith("acct_", StringComparison.Ordinal))
      .WithMessage("ending_before must be an Account id starting with 'acct_'.");
  }
}
