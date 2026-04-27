using Hex.Scaffold.Domain.AccountAggregate;

namespace Hex.Scaffold.Adapters.Persistence.PostgreSql.Config;

// EF Core mapping for the Account aggregate. Column names are snake_case to
// stay aligned with the wire format and with Stripe's database conventions
// — the v2 API surface is snake_case end-to-end and there's no reason to
// translate at the persistence boundary.
//
// Nested-object columns are `jsonb` so Postgres can validate the structure
// at write time and so that callers can run `->>` / `@>` queries against
// `configuration`, `identity`, etc. without us having to mirror Stripe's
// 80+ nested types as relational tables. The `string` <-> `jsonb` round
// trip works because PostgreSqlServiceExtensions calls EnableDynamicJson()
// on the NpgsqlDataSourceBuilder; without that flag Npgsql rejects raw
// strings against jsonb parameters.
//
// PR #20's lesson on Vogen-typed PKs still applies: AccountId is a string-
// typed Vogen value object generated inside Account.Create() before the
// entity ever hits ChangeTracker, so EF's IdentityMap.Add never sees a
// default(AccountId). No HiLo, no ISampleIdGenerator port — the keying is
// done in the domain.
public class AccountConfiguration : IEntityTypeConfiguration<Account>
{
  public void Configure(EntityTypeBuilder<Account> builder)
  {
    builder.ToTable("accounts");

    builder.HasKey(x => x.Id);
    builder.Property(x => x.Id)
      .HasColumnName("id")
      .HasMaxLength(64)
      .HasConversion(
        id => id.Value,
        value => AccountId.From(value));

    builder.Property(x => x.Livemode).HasColumnName("livemode");
    builder.Property(x => x.Created).HasColumnName("created");
    builder.Property(x => x.Closed).HasColumnName("closed");
    builder.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(200);
    builder.Property(x => x.ContactEmail).HasColumnName("contact_email").HasMaxLength(254);
    builder.Property(x => x.ContactPhone).HasColumnName("contact_phone").HasMaxLength(40);
    builder.Property(x => x.Dashboard).HasColumnName("dashboard").HasMaxLength(16);

    // Native Postgres text[] — supports the queryable
    // `WHERE 'merchant' = ANY(applied_configurations)` pattern callers use
    // to route Connect-style flows (Direct vs Destination charges).
    builder.Property(x => x.AppliedConfigurations)
      .HasColumnName("applied_configurations")
      .HasColumnType("text[]");

    // Stripe-shaped nested blobs round-tripped as raw JSON strings. Storing
    // as `jsonb` rather than `text` gives us free Postgres-side validation
    // (rejects malformed JSON at write time) plus path-expression queries
    // when consumers need them.
    builder.Property(x => x.ConfigurationJson)
      .HasColumnName("configuration").HasColumnType("jsonb");
    builder.Property(x => x.IdentityJson)
      .HasColumnName("identity").HasColumnType("jsonb");
    builder.Property(x => x.DefaultsJson)
      .HasColumnName("defaults").HasColumnType("jsonb");
    builder.Property(x => x.RequirementsJson)
      .HasColumnName("requirements").HasColumnType("jsonb");
    builder.Property(x => x.FutureRequirementsJson)
      .HasColumnName("future_requirements").HasColumnType("jsonb");
    builder.Property(x => x.MetadataJson)
      .HasColumnName("metadata").HasColumnType("jsonb");

    // Cursor pagination on (created DESC, id DESC) — the index supports the
    // ListAccountsQueryService's keyset query without falling back to a
    // full sort. Postgres-specific `OPERATIONS DESC` would be tighter, but
    // EF's `IsDescending` is portable and good enough.
    builder.HasIndex(x => new { x.Created, x.Id })
      .IsDescending(true, true)
      .HasDatabaseName("ix_accounts_created_id_desc");
  }
}
