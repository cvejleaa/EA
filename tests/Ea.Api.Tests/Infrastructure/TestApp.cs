using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ea.Api.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Ea.Api.Tests.Infrastructure;

public sealed record TestUser(string Id, string Oid, string Name, params string[] Roles);

public static class TestUsers
{
    public const string SigningKey = "test-noegle-kun-til-automatiske-tests-0123456789abcdef";

    public static readonly TestUser Admin = new("eva", "00000000-0000-0000-0000-0000000000a1", "Eva Arkitekt", AppRoles.Admin);
    public static readonly TestUser Reader = new("leo", "00000000-0000-0000-0000-0000000000b2", "Leo Læser");

    public static readonly TestUser[] All = [Admin, Reader];
}

/// <summary>Én kørende API-instans mod sin egen database. Brug: <c>await using var app = await TestApp.StartAsync();</c></summary>
public sealed class TestApp : IAsyncDisposable
{
    public static readonly DateTimeOffset Start = new(2026, 9, 1, 10, 0, 0, TimeSpan.Zero);

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly Factory _factory;
    private readonly string _database;

    private TestApp(Factory factory, string database, FakeTimeProvider time)
    {
        _factory = factory;
        _database = database;
        Time = time;
    }

    public FakeTimeProvider Time { get; }

    public IServiceProvider Services => _factory.Services;

    public static async Task<TestApp> StartAsync(string environment = "Testing", string authMode = AuthSetup.DevMode)
    {
        var database = await TestDatabase.CreateAsync();
        var time = new FakeTimeProvider(Start);
        var factory = new Factory(TestDatabase.ConnectionStringFor(database), time, environment, authMode);
        var app = new TestApp(factory, database, time);
        try
        {
            _ = factory.Services; // Start værten nu, så opstartsfejl viser sig her.
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }

        return app;
    }

    public HttpClient Anonymous() => _factory.CreateClient();

    public async Task<HttpClient> ClientFor(TestUser user)
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/dev/token", new DevTokenRequest(user.Id), Json);
        response.EnsureSuccessStatusCode();
        var token = await response.Content.ReadFromJsonAsync<DevTokenResponse>(Json);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.AccessToken);
        return client;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _factory.DisposeAsync();
        }
        finally
        {
            await TestDatabase.DropAsync(_database);
        }
    }

    private sealed class Factory(string connectionString, FakeTimeProvider time, string environment, string authMode)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment);
            builder.UseSetting("ConnectionStrings:Ea", connectionString);
            builder.UseSetting(AuthSetup.ModeKey, authMode);
            builder.UseSetting("Auth:Dev:SigningKey", TestUsers.SigningKey);
            for (var i = 0; i < TestUsers.All.Length; i++)
            {
                var user = TestUsers.All[i];
                builder.UseSetting($"Auth:Dev:Users:{i}:Id", user.Id);
                builder.UseSetting($"Auth:Dev:Users:{i}:Oid", user.Oid);
                builder.UseSetting($"Auth:Dev:Users:{i}:Name", user.Name);
                for (var r = 0; r < user.Roles.Length; r++)
                {
                    builder.UseSetting($"Auth:Dev:Users:{i}:Roles:{r}", user.Roles[r]);
                }
            }

            builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(time));
        }
    }
}
