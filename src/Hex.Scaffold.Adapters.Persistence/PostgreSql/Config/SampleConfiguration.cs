using Hex.Scaffold.Domain.SampleAggregate;

namespace Hex.Scaffold.Adapters.Persistence.PostgreSql.Config;

public class SampleConfiguration : IEntityTypeConfiguration<Sample>
{
  public void Configure(EntityTypeBuilder<Sample> builder)
  {
    builder.HasKey(x => x.Id);

    // Sample.Id starts as `default(SampleId)` — Vogen's uninitialized struct
    // sentinel, which throws on any access to .Value. EF Core's IdentityMap
    // calls ValueComparer<SampleId>.GetHashCode immediately on Add, before
    // SaveChanges, which detonates that sentinel.
    //
    // PR #14 tried `.UseHiLo` paired with `.HasConversion`, expecting Hi-Lo
    // to materialize a real id before IdentityMap.Add. That FAILED at
    // runtime because for KEY properties EF's hashing step runs BEFORE the
    // value-converter chain (and therefore before Hi-Lo). The Vogen
    // ThrowHelper still fired.
    //
    // Custom ValueGenerator<SampleId> — see SampleIdValueGenerator.cs — runs
    // EARLIER in the pipeline (during EntityGraphAttacher), gated only on
    // `HasValueGenerator` + `ValueGeneratedOnAdd`, NOT on the value
    // converter. By the time IdentityMap asks for the hash, the property
    // already holds a valid SampleId pulled from `samples_hilo_seq`.
    builder.Property(x => x.Id)
      .HasConversion(
        id => id.Value,
        value => SampleId.From(value))
      .HasValueGenerator<SampleIdValueGenerator>()
      .ValueGeneratedOnAdd()
      // Anchor the model snapshot to the existing samples_hilo_seq sequence
      // (created by the AddSampleIdHiLoSequence migration in PR #14). The
      // Postgres-side sequence is still the source of ids — only the C# wiring
      // of how EF READS from it changed (custom ValueGenerator instead of
      // EF's built-in HiLoValueGenerator). At runtime SampleIdValueGenerator
      // wins (HasValueGenerator above takes precedence over .UseHiLo's built-
      // in generator); at snapshot/migration time the .UseHiLo annotation
      // tells EF "this column is already keyed off samples_hilo_seq" so the
      // diff against the pre-existing snapshot is EMPTY.
      // Without this anchor, EF's default value-generation strategy for int
      // PKs is IDENTITY-by-default, and the next `dotnet ef migrations add`
      // would propose dropping the sequence and altering the column —
      // breaking SampleIdValueGenerator at the next helm upgrade and tripping
      // PendingModelChangesWarning (escalated to error by TreatWarningsAsErrors)
      // on every existing migration runner pod.
      .UseHiLo("samples_hilo_seq");

    builder.Property(x => x.Name)
      .HasMaxLength(SampleName.MaxLength)
      .HasConversion(
        name => name.Value,
        value => SampleName.From(value));

    builder.Property(x => x.Status)
      .HasConversion(
        status => status.Value,
        value => SampleStatus.FromValue(value));

    builder.Property(x => x.Description)
      .HasMaxLength(1000)
      .IsRequired(false);
  }
}
