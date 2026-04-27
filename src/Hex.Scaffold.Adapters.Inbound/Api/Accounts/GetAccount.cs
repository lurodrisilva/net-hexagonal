using Hex.Scaffold.Adapters.Inbound.Api.Extensions;
using Hex.Scaffold.Application.Accounts;
using Hex.Scaffold.Application.Accounts.Get;
using Hex.Scaffold.Domain.AccountAggregate;

namespace Hex.Scaffold.Adapters.Inbound.Api.Accounts;

// GET /v2/core/accounts/{id}
public class GetAccount(IMediator mediator)
  : Endpoint<GetAccountRequest, Results<Ok<AccountDto>, NotFound, ProblemHttpResult>>
{
  public override void Configure()
  {
    Get("/v2/core/accounts/{id}");
    AllowAnonymous();
    Summary(s =>
    {
      s.Summary = "Retrieve an Account";
      s.Description = "Reproduces Stripe v2 GET /v2/core/accounts/{id}.";
      s.Responses[200] = "Account found";
      s.Responses[404] = "Account not found";
    });
    Tags("Accounts");
  }

  public override async Task<Results<Ok<AccountDto>, NotFound, ProblemHttpResult>>
    ExecuteAsync(GetAccountRequest request, CancellationToken ct)
  {
    var result = await mediator.Send(
      new GetAccountQuery(AccountId.From(request.Id!)), ct);
    return result.ToGetByIdResult(dto => dto);
  }
}

public class GetAccountRequest
{
  public string? Id { get; set; }
}

public class GetAccountValidator : Validator<GetAccountRequest>
{
  public GetAccountValidator()
  {
    RuleFor(x => x.Id)
      .NotEmpty()
      .Must(id => id is null || id.StartsWith("acct_", StringComparison.Ordinal))
      .WithMessage("Account id must start with 'acct_'.");
  }
}
