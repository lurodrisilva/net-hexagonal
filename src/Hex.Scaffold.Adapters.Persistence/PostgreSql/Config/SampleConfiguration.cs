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
      .ValueGeneratedOnAdd();

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
