using Hex.Scaffold.Domain.SampleAggregate;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace Hex.Scaffold.Adapters.Persistence.PostgreSql.Config;

// Custom ValueGenerator<SampleId> — paired with .HasValueGenerator<>() in
// SampleConfiguration so EF runs this BEFORE IdentityMap.Add ever asks the
// ValueComparer for a hash. The previous attempt (.UseHiLo + .HasConversion)
// failed at runtime because EF's value-converter chain runs AFTER the key
// hashing step for keyed properties — Vogen detonated on default(SampleId)
// before Hi-Lo got a chance to materialize a real id. ValueGenerators run
// earlier in the pipeline and are not gated by the converter.
//
// We pull the next id directly from the same Postgres sequence the previous
// approach created (`samples_hilo_seq`). One round-trip per insert; if that
// becomes a hotspot, swap to a fenced HiLoValueGenerator that batches 10 ids.
public sealed class SampleIdValueGenerator : ValueGenerator<SampleId>
{
  private const string NextValSql = "SELECT nextval('samples_hilo_seq')";

  public override bool GeneratesTemporaryValues => false;

  public override SampleId Next(EntityEntry entry)
  {
    var connection = entry.Context.Database.GetDbConnection();
    if (connection.State != System.Data.ConnectionState.Open)
      connection.Open();

    using var command = connection.CreateCommand();
    command.CommandText = NextValSql;
    var nextId = Convert.ToInt32((long)command.ExecuteScalar()!);
    return SampleId.From(nextId);
  }
}
