using Hex.Scaffold.Application;
using Hex.Scaffold.Application.Samples;
using Hex.Scaffold.Application.Samples.List;

namespace Hex.Scaffold.Adapters.Inbound.Api.Samples;

public class List(IMediator mediator)
  : Endpoint<ListSamplesRequest, Ok<Hex.Scaffold.Application.PagedResult<SampleRecord>>>
{
  public override void Configure()
  {
    Get("/samples");
    AllowAnonymous();
    Summary(s =>
    {
      s.Summary = "List samples";
      s.Responses[200] = "Paginated sample list";
    });
    Tags("Samples");
  }

  public override async Task<Ok<Hex.Scaffold.Application.PagedResult<SampleRecord>>>
    ExecuteAsync(ListSamplesRequest request, CancellationToken ct)
  {
    var result = await mediator.Send(
      new ListSamplesQuery(request.Page, request.PerPage), ct);

    var records = new Hex.Scaffold.Application.PagedResult<SampleRecord>(
      result.Value.Items.Select(d => new SampleRecord(
        d.Id.Value, d.Name.Value, d.Status.Name, d.Description)).ToList(),
      result.Value.Page,
      result.Value.PerPage,
      result.Value.TotalCount,
      result.Value.TotalPages);

    return TypedResults.Ok(records);
  }
}

public class ListSamplesRequest
{
  public int? Page { get; set; } = 1;
  public int? PerPage { get; set; } = Constants.DefaultPageSize;
}
