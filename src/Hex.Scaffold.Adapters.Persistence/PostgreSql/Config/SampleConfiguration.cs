using Hex.Scaffold.Domain.SampleAggregate;

namespace Hex.Scaffold.Adapters.Persistence.PostgreSql.Config;

public class SampleConfiguration : IEntityTypeConfiguration<Sample>
{
  public void Configure(EntityTypeBuilder<Sample> builder)
  {
    builder.HasKey(x => x.Id);

    // Sample.Id is now assigned by ISampleIdGenerator in the application
    // handler BEFORE the entity is added to ChangeTracker, so by the time
    // EF hashes the key in IdentityMap.Add it holds a Vogen-initialized
    // SampleId and the previous "Use of uninitialized Value Object" crash
    // can no longer fire. PRs #14 and #18 both tried to fix this from the
    // EF mapping layer (UseHiLo, then HasValueGenerator) and both failed:
    // EF gates value-generation on KeyComparer.Equals(currentValue,
    // default(SampleId)) returning true, but Vogen-generated Equals returns
    // false on uninitialized structs, so EF concluded the key was already
    // set, skipped the generator, and crashed in the very next step.
    //
    // The Postgres `samples_hilo_seq` sequence (created by the
    // AddSampleIdHiLoSequence migration in PR #14) is still the source of
    // ids — SampleIdGenerator pulls from it directly. The .UseHiLo
    // annotation below stays purely as a snapshot anchor: it tells EF
    // "this column is keyed off samples_hilo_seq" so `dotnet ef migrations
    // add` produces an empty diff. Removing it would have EF default to
    // IDENTITY-by-default and propose dropping the sequence on the next
    // migration, breaking SampleIdGenerator.
    builder.Property(x => x.Id)
      .HasConversion(
        id => id.Value,
        value => SampleId.From(value))
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
