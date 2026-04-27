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
    // Hi-Lo runs inside SetEntityStateAsync(Added) BEFORE IdentityMap.Add:
    // it pulls a positive int from `samples_hilo_seq`, the converter below
    // wraps it in a valid SampleId, and only then does IdentityMap see a
    // properly-initialized struct. Sequence starts at 1 (Vogen requires > 0)
    // and is created by the AddSampleIdHiLoSequence migration.
    builder.Property(x => x.Id)
      .HasConversion(
        id => id.Value,
        value => SampleId.From(value))
      .UseHiLo("samples_hilo_seq")
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
