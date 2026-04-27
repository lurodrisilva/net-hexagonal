using System.Text.Json;
using Hex.Scaffold.Application.Accounts;
using Hex.Scaffold.Application.Accounts.Update;
using Hex.Scaffold.Domain.AccountAggregate;

namespace Hex.Scaffold.Adapters.Inbound.Api.Accounts;

// POST /v2/core/accounts/{id} — Stripe uses POST for both create and
// partial-update (idempotent in practice via the Idempotency-Key header).
// 200 OK on success, 404 when the id doesn't exist.
public class UpdateAccount(IMediator mediator)
  : Endpoint<UpdateAccountRequest, Results<Ok<AccountDto>, NotFound, ProblemHttpResult>>
{
  public override void Configure()
  {
    Post("/v2/core/accounts/{id}");
    AllowAnonymous();
    Summary(s =>
    {
      s.Summary = "Update an Account";
      s.Description = "Reproduces Stripe v2 POST /v2/core/accounts/{id}.";
      s.Responses[200] = "Account updated";
      s.Responses[404] = "Account not found";
    });
    Tags("Accounts");
  }

  public override async Task<Results<Ok<AccountDto>, NotFound, ProblemHttpResult>>
    ExecuteAsync(UpdateAccountRequest request, CancellationToken ct)
  {
    var command = new UpdateAccountCommand(
      Id: AccountId.From(request.Id!),
      DisplayName:           request.DisplayName.ToMaybeString(),
      ContactEmail:          request.ContactEmail.ToMaybeString(),
      ContactPhone:          request.ContactPhone.ToMaybeString(),
      AppliedConfigurations: request.AppliedConfigurations.ToMaybeAppliedConfigs(),
      ConfigurationJson:     request.Configuration.ToMaybeRawJson(),
      IdentityJson:          request.Identity.ToMaybeRawJson(),
      DefaultsJson:          request.Defaults.ToMaybeRawJson(),
      MetadataJson:          request.Metadata.ToMaybeRawJson());

    var result = await mediator.Send(command, ct);
    if (result.IsSuccess) return TypedResults.Ok(result.Value);
    if (result.Status == ResultStatus.NotFound) return TypedResults.NotFound();
    return TypedResults.Problem(result.Errors.FirstOrDefault() ?? "An error occurred.");
  }
}

// All fields default to JsonValueKind.Undefined; the helpers in
// AccountFieldHelpers turn that into "omitted, leave alone".
public class UpdateAccountRequest
{
  public string? Id { get; set; } // route binding

  public JsonElement DisplayName { get; set; }
  public JsonElement ContactEmail { get; set; }
  public JsonElement ContactPhone { get; set; }
  public JsonElement AppliedConfigurations { get; set; }
  public JsonElement Configuration { get; set; }
  public JsonElement Identity { get; set; }
  public JsonElement Defaults { get; set; }
  public JsonElement Metadata { get; set; }
}

public class UpdateAccountValidator : Validator<UpdateAccountRequest>
{
  public UpdateAccountValidator()
  {
    RuleFor(x => x.Id)
      .NotEmpty()
      .Must(id => id is null || id.StartsWith("acct_", StringComparison.Ordinal))
      .WithMessage("Account id must start with 'acct_'.");

    // contact_email: format-only on update (the create-time merchant/
    // recipient required-ness is one-shot at creation; mutating an account
    // shouldn't re-impose it).
    RuleFor(x => x.ContactEmail)
      .Must(el => el.ValueKind != JsonValueKind.String
                  || string.IsNullOrEmpty(el.GetString())
                  || new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(el.GetString()))
      .WithMessage("contact_email must be a valid email address.");
  }
}
