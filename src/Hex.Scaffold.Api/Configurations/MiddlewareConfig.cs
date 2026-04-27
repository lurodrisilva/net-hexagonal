using System.Text.Json;
using Hex.Scaffold.Adapters.Persistence.PostgreSql;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Scalar.AspNetCore;

namespace Hex.Scaffold.Api.Configurations;

public static class MiddlewareConfig
{
  public static async Task<WebApplication> UseAppMiddlewareAsync(this WebApplication app)
  {
    if (app.Environment.IsDevelopment())
    {
      app.UseDeveloperExceptionPage();
    }
    else
    {
      app.UseExceptionHandler();
      app.UseStatusCodePages();
      app.UseHsts();
    }

    app.UseHttpsRedirection();

    // TODO: Add authentication/authorization middleware here

    app.UseRateLimiter();

    app.UseFastEndpoints(c =>
    {
      c.Errors.UseProblemDetails();
      // Stripe v2 wire format is snake_case end-to-end. Setting the
      // serializer policy here means C# PascalCase property names on
      // AccountDto / Create/UpdateAccountRequest / AccountListResult map
      // onto `applied_configurations`, `contact_email`, `display_name`,
      // `starting_after`, `has_more`, etc. without [JsonPropertyName]
      // decorations. Affects request binding too — incoming
      // `applied_configurations` deserializes onto the C#
      // AppliedConfigurations property.
      c.Serializer.Options.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
      c.Serializer.Options.DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower;
    });

    if (app.Environment.IsDevelopment())
    {
      app.UseSwaggerGen();
      app.MapScalarApiReference();
    }

    // Health checks
    app.MapHealthChecks("/healthz", new HealthCheckOptions
    {
      Predicate = r => r.Tags.Contains("live")
    });
    app.MapHealthChecks("/ready", new HealthCheckOptions
    {
      Predicate = r => r.Tags.Contains("ready")
    });

    // Apply migrations if configured
    var applyMigrations = app.Configuration.GetValue<bool>("Database:ApplyMigrationsOnStartup");
    if (applyMigrations || app.Environment.IsDevelopment())
    {
      using var scope = app.Services.CreateScope();
      var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
      await db.Database.MigrateAsync();
    }

    return app;
  }
}
