using System.Text.Json.Serialization;
using Ea.Api.Auth;
using Ea.Api.CurrentUser;
using Ea.Api.Data;
using Ea.Api.Persons;
using Ea.Api.Systems;
using Ea.Api.Teams;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEaDatabase(builder.Configuration);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks().AddDbContextCheck<EaDbContext>();
builder.Services.ConfigureHttpJsonOptions(o =>
{
    // Enums som tekst i API'et (fx "IDrift") — samme navne som i databasen og CSV-skabelonen.
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    // Tal er tal (ikke "tal eller tekst") — giver en stram kontrakt og rene TypeScript-typer.
    o.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
});
builder.AddEaAuth();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
{
    app.MapOpenApi().AllowAnonymous();
}

app.MapHealthChecks("/healthz").AllowAnonymous();
app.MapAuthEndpoints();
app.MapMeEndpoints();
app.MapTeamEndpoints();
app.MapPersonEndpoints();
app.MapSystemEndpoints();

if (app.Environment.IsDevelopment())
{
    // Kun lokalt. I produktion køres migrationer som et separat trin før appen (migrations bundle).
    await DevSeed.MigrateAndSeedAsync(app.Services);
}

await app.RunAsync();

/// <summary>Synlig for WebApplicationFactory i testene.</summary>
public partial class Program;
