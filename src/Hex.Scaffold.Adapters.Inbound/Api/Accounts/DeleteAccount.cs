using Hex.Scaffold.Adapters.Inbound.Api.Extensions;
using Hex.Scaffold.Application.Accounts.Delete;
using Hex.Scaffold.Domain.AccountAggregate;

namespace Hex.Scaffold.Adapters.Inbound.Api.Accounts;

// DELETE /v2/core/accounts/{id} — hard-delete a single account.
// 204 NoContent on success (including idempotent delete of a missing account),
// 404 is never returned because the handler treats a missing account as a
// successful no-op (at-least-once Kafka delivery safety).
public class DeleteAccount(IMediator mediator)
  : Endpoint<DeleteAccountRequest, Results<NoContent, NotFound, ProblemHttpResult>>
{
  public override void Configure()
  {
    Delete("/v2/core/accounts/{id}");
    AllowAnonymous();
    Summary(s =>
    {
      s.Summary = "Delete an Account";
      s.Description = "Hard-deletes an account by id. Idempotent — deleting a non-existent account returns 204.";
      s.Responses[204] = "Account deleted";
    });
    Tags("Accounts");
  }

  public override async Task<Results<NoContent, NotFound, ProblemHttpResult>>
    ExecuteAsync(DeleteAccountRequest request, CancellationToken ct)
  {
    var command = new DeleteAccountCommand(AccountId.From(request.Id!));
    var result = await mediator.Send(command, ct);
    return result.ToDeleteResult();
  }
}

public class DeleteAccountRequest
{
  public string? Id { get; set; } // route binding
}

public class DeleteAccountValidator : Validator<DeleteAccountRequest>
{
  public DeleteAccountValidator()
  {
    RuleFor(x => x.Id)
      .NotEmpty()
      .Must(id => id is null || id.StartsWith("acct_", StringComparison.Ordinal))
      .WithMessage("Account id must start with 'acct_'.");
  }
}
