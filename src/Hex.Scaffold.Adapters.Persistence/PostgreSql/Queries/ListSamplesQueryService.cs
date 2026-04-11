using System.Data;
using Dapper;
using Hex.Scaffold.Application;
using Hex.Scaffold.Application.Samples;
using Hex.Scaffold.Application.Samples.List;
using Hex.Scaffold.Domain.SampleAggregate;
using Npgsql;

namespace Hex.Scaffold.Adapters.Persistence.PostgreSql.Queries;

public sealed class ListSamplesQueryService(IConfiguration _configuration)
  : IListSamplesQueryService
{
  public async Task<PagedResult<SampleDto>> ListAsync(
    int page,
    int perPage,
    CancellationToken cancellationToken = default)
  {
    var connectionString = _configuration.GetConnectionString("PostgreSql")
      ?? throw new InvalidOperationException("PostgreSql connection string not found.");

    var offset = (page - 1) * perPage;

    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync(cancellationToken);

    var items = (await connection.QueryAsync<(int Id, string Name, int StatusValue, string? Description)>(
      """
      SELECT "Id", "Name", "Status", "Description"
      FROM "Samples"
      ORDER BY "Id"
      OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
      """,
      new { Offset = offset, PageSize = perPage })).ToList();

    var totalCount = await connection.ExecuteScalarAsync<int>(
      """SELECT COUNT(*) FROM "Samples" """);

    var totalPages = (int)Math.Ceiling(totalCount / (double)perPage);

    var dtos = items.Select(x => new SampleDto(
      SampleId.From(x.Id),
      SampleName.From(x.Name),
      SampleStatus.FromValue(x.StatusValue),
      x.Description)).ToList();

    return new PagedResult<SampleDto>(dtos, page, perPage, totalCount, totalPages);
  }
}
