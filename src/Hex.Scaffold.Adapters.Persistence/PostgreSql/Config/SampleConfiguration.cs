using Hex.Scaffold.Domain.SampleAggregate;

namespace Hex.Scaffold.Adapters.Persistence.PostgreSql.Config;

public class SampleConfiguration : IEntityTypeConfiguration<Sample>
{
  public void Configure(EntityTypeBuilder<Sample> builder)
  {
    builder.HasKey(x => x.Id);

    builder.Property(x => x.Id)
      .HasConversion(
        id => id.Value,
        value => SampleId.From(value));

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
