using System.Text.Json;
using Hex.Scaffold.Application.Accounts;
using Hex.Scaffold.Application.Accounts.Create;

namespace Hex.Scaffold.Adapters.Inbound.Api.Accounts;

// POST /v2/core/accounts — Stripe-faithful: 200 OK on success (Stripe does
// not return 201 for resource creation), Account JSON in the body. The
// FastEndpoints serializer is configured at composition root with the
// snake_case_lower naming policy so the C# PascalCase property names map
// 1:1 onto Stripe's snake_case wire shape without [JsonPropertyName].
public class CreateAccount(IMediator mediator)
  : Endpoint<CreateAccountRequest, Results<Ok<AccountDto>, ProblemHttpResult>>
{
  public override void Configure()
  {
    Post("/v2/core/accounts");
    AllowAnonymous();
    Summary(s =>
    {
      s.Summary = "Create an Account";
      s.Description = "Reproduces Stripe v2 POST /v2/core/accounts.";
      s.Responses[200] = "Account created";
      s.Responses[400] = "Invalid request";
    });
    Tags("Accounts");
  }

  public override async Task<Results<Ok<AccountDto>, ProblemHttpResult>>
    ExecuteAsync(CreateAccountRequest request, CancellationToken ct)
  {
    var command = new CreateAccountCommand(
      Livemode: request.Livemode ?? false,
      DisplayName: request.DisplayName,
      ContactEmail: request.ContactEmail,
      ContactPhone: request.ContactPhone,
      AppliedConfigurations: AccountFieldHelpers.ToAppliedConfigs(request.AppliedConfigurations),
      ConfigurationJson: SerializeOrNull(request.Configuration),
      IdentityJson:      SerializeOrNull(request.Identity),
      DefaultsJson:      SerializeOrNull(request.Defaults),
      MetadataJson:      SerializeOrNull(request.Metadata));

    var result = await mediator.Send(command, ct);
    if (result.IsSuccess)
      return TypedResults.Ok(result.Value);
    return TypedResults.Problem(result.Errors.FirstOrDefault() ?? "An error occurred.");
  }

  // The aggregate stores nested objects as raw JSON strings — the
  // serializer here uses GetRawText so we don't lose the caller's exact
  // formatting. Null/absent → null column.
  private static string? SerializeOrNull(JsonElement el) =>
    el.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ? null : el.GetRawText();
}

public class CreateAccountRequest
{
  // Top-level scalars. Livemode defaults to false in the aggregate when
  // omitted; live calls are gated by the API key environment, not the
  // request body.
  public bool? Livemode { get; set; }
  public string? DisplayName { get; set; }
  public string? ContactEmail { get; set; }
  public string? ContactPhone { get; set; }

  // Wire format is an array of strings. Bound via System.Text.Json with
  // the snake_case_lower naming policy — incoming key is
  // applied_configurations.
  public List<string>? AppliedConfigurations { get; set; }

  // Nested objects: kept as JsonElement so the persistence layer can store
  // them verbatim (jsonb) without modeling Stripe's full nested type
  // surface. JsonValueKind.Undefined when the key is absent.
  public JsonElement Configuration { get; set; }
  public JsonElement Identity { get; set; }
  public JsonElement Defaults { get; set; }
  public JsonElement Metadata { get; set; }
}

public class CreateAccountValidator : Validator<CreateAccountRequest>
{
  public CreateAccountValidator()
  {
    RuleFor(x => x.DisplayName).MaximumLength(200);
    RuleFor(x => x.ContactPhone).MaximumLength(40);
    RuleFor(x => x.ContactEmail)
      .EmailAddress().When(x => !string.IsNullOrEmpty(x.ContactEmail))
      .MaximumLength(254);

    // Stripe documents: contact_email is required when configuring the
    // account as a merchant or recipient. Customer-only accounts may omit
    // it. Reproduced here so callers see the same 4xx surface.
    RuleFor(x => x.ContactEmail)
      .NotEmpty()
      .When(x => x.AppliedConfigurations is { } configs
                 && configs.Any(c => c is "merchant" or "recipient"))
      .WithMessage("contact_email is required when applied_configurations contains 'merchant' or 'recipient'.");
  }
}
