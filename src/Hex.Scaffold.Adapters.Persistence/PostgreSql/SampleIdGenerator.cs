using System.Data;
using Hex.Scaffold.Domain.Ports.Outbound;
using Hex.Scaffold.Domain.SampleAggregate;

namespace Hex.Scaffold.Adapters.Persistence.PostgreSql;

// EF's value-generation pipeline is unreachable for Vogen-typed keys: its
// "is the key unknown?" probe calls KeyComparer.Equals(default, default),
// Vogen-generated Equals returns false on uninitialized structs, so EF
// concludes the key is already set, skips the generator, and detonates in
// IdentityMap.Add.GetHashCode(default(SampleId)). PRs #14 and #18 both
// tripped that wire — see fix/sample-id-domain-generator commit message.
//
// We sidestep EF entirely: pull the next id from the existing
// `samples_hilo_seq` Postgres sequence (created by the
// AddSampleIdHiLoSequence migration in PR #14, still applied) and assign it
// to Sample.Id BEFORE the entity is added to ChangeTracker. By the time EF
// hashes the key, it's a valid Vogen-initialized struct.
//
// The sequence is shared by the EF model snapshot (.UseHiLo annotation in
// SampleConfiguration) so `dotnet ef migrations add` still produces an empty
// diff. The DB call runs on the same AppDbContext connection, so Npgsql/EF
// OpenTelemetry instrumentation traces it as a child span of the current
// HTTP activity — App Map, Live Metrics, and the dependency timeline keep
// rendering it without changes.
internal sealed class SampleIdGenerator(AppDbContext db) : ISampleIdGenerator
{
  private const string NextValSql = "SELECT nextval('samples_hilo_seq')";

  public async Task<SampleId> NextAsync(CancellationToken cancellationToken = default)
  {
    var connection = db.Database.GetDbConnection();
    if (connection.State != ConnectionState.Open)
      await connection.OpenAsync(cancellationToken);

    await using var command = connection.CreateCommand();
    command.CommandText = NextValSql;
    var raw = (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    return SampleId.From((int)raw);
  }
}
