using System.Net;
using System.Net.Http.Json;
using Ea.Api.Auth;
using Ea.Api.CurrentUser;
using Ea.Api.Persons;
using Ea.Api.Systems;
using Ea.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Ea.Api.Tests.Authorization;

public sealed class AuthorizationTests
{
    /// <summary>De ENESTE endpoints, der må kaldes uden login. Alt andet skal afvises.</summary>
    private static readonly HashSet<string> AnonymousAllowList =
    [
        "* /healthz",
        "GET /api/auth/mode",
        "GET /api/dev/users",
        "POST /api/dev/token",
        "GET /openapi/{documentName}.json",
    ];

    private static List<(string Method, string Route, bool Anonymous)> Endpoints(TestApp app) =>
        app.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .SelectMany(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["*"])
                .Select(m => (m, "/" + e.RoutePattern.RawText!.TrimStart('/'),
                    e.Metadata.GetMetadata<IAllowAnonymous>() is not null)))
            .ToList();

    [Fact]
    public async Task Kun_endpoints_paa_allow_listen_er_aabne_uden_login()
    {
        await using var app = await TestApp.StartAsync();

        var anonymous = Endpoints(app)
            .Where(e => e.Anonymous)
            .Select(e => $"{e.Method} {e.Route}")
            .ToHashSet();

        // Begge veje: intet nyt åbent endpoint, og allow-listen peger ikke på noget, der er forsvundet.
        Assert.Equal(AnonymousAllowList.Order(), anonymous.Order());
    }

    [Fact]
    public async Task Anonym_faar_401_paa_alle_beskyttede_api_endpoints()
    {
        await using var app = await TestApp.StartAsync();
        var client = app.Anonymous();

        var protectedEndpoints = Endpoints(app)
            .Where(e => !e.Anonymous && e.Route.StartsWith("/api/", StringComparison.Ordinal))
            .ToList();

        // En løkke over ingenting beviser ingenting.
        Assert.True(protectedEndpoints.Count >= 9, $"Fandt kun {protectedEndpoints.Count} beskyttede endpoints.");

        foreach (var (method, route, _) in protectedEndpoints)
        {
            var url = route.Replace("{id:guid}", Guid.NewGuid().ToString(), StringComparison.Ordinal);
            using var request = new HttpRequestMessage(new HttpMethod(method), url)
            {
                Content = method is "POST" or "PUT" ? JsonContent.Create(new { }) : null,
            };
            var response = await client.SendAsync(request);
            Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, $"{method} {route} gav {(int)response.StatusCode}");
        }
    }

    [Fact]
    public async Task Laeser_kan_laese_men_ikke_skrive_og_intet_aendres()
    {
        await using var app = await TestApp.StartAsync();
        var admin = await app.ClientFor(TestUsers.Admin);
        var reader = await app.ClientFor(TestUsers.Reader);
        var system = await admin.CreateSystemAsync("Kompas");

        Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync($"/api/systems/{system.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync("/api/systems")).StatusCode);

        var create = await reader.PostAsJsonAsync("/api/systems", TestApi.NewSystem("Nyt"), TestApp.Json);
        var update = await reader.PutSystemAsync(system.Id, system.ToWrite() with { Name = "Omdøbt" });
        var confirm = await reader.PostAsJsonAsync($"/api/systems/{system.Id}/confirm", new ConfirmSystemRequest(system.Version), TestApp.Json);
        var delete = await reader.DeleteAsync($"/api/systems/{system.Id}");
        var person = await reader.PostAsJsonAsync("/api/persons", new CreatePersonRequest("X", null, null), TestApp.Json);

        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, update.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, confirm.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, person.StatusCode);

        var after = await admin.GetSystemAsync(system.Id);
        Assert.Equal("Kompas", after.Name);
        Assert.Equal(system.Version, after.Version);
        Assert.Equal(1, (await admin.ListSystemsAsync()).Total);
    }

    [Fact]
    public async Task Permissions_afspejler_rollen()
    {
        await using var app = await TestApp.StartAsync();
        var admin = await app.ClientFor(TestUsers.Admin);
        var reader = await app.ClientFor(TestUsers.Reader);
        var system = await admin.CreateSystemAsync("Kompas");

        var asReader = await reader.GetSystemAsync(system.Id);
        Assert.False(asReader.Permissions.CanEdit);
        Assert.False(asReader.Permissions.CanDelete);

        var asAdmin = await admin.GetSystemAsync(system.Id);
        Assert.True(asAdmin.Permissions.CanEdit);
        Assert.True(asAdmin.Permissions.CanDelete);

        var meReader = await reader.GetFromJsonAsync<MeResponse>("/api/me", TestApp.Json);
        var meAdmin = await admin.GetFromJsonAsync<MeResponse>("/api/me", TestApp.Json);
        Assert.Equal(new MePermissions(false, false), meReader!.Permissions);
        Assert.Equal(new MePermissions(true, true), meAdmin!.Permissions);
        Assert.Equal(TestUsers.Admin.Oid, meAdmin.Oid);
        Assert.Equal([AppRoles.Admin], meAdmin.Roles);
    }

    [Fact]
    public async Task Dev_login_kan_ikke_startes_i_produktion()
    {
        var ex = await Record.ExceptionAsync(() => TestApp.StartAsync(environment: "Production"));

        Assert.NotNull(ex);
        Assert.Contains(Flatten(ex), e =>
            e is InvalidOperationException && e.Message.Contains("Auth:Mode=Dev er kun tilladt", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Entra_mode_fejler_lukket_indtil_det_er_sat_op()
    {
        var ex = await Record.ExceptionAsync(() => TestApp.StartAsync(authMode: AuthSetup.EntraMode));

        Assert.NotNull(ex);
        Assert.Contains(Flatten(ex), e => e is NotSupportedException);
    }

    [Fact]
    public async Task Ukendt_dev_bruger_faar_ikke_et_token()
    {
        await using var app = await TestApp.StartAsync();
        var response = await app.Anonymous().PostAsJsonAsync("/api/dev/token", new DevTokenRequest("ukendt"), TestApp.Json);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Token_med_forkert_noegle_afvises()
    {
        await using var app = await TestApp.StartAsync();
        var client = app.Anonymous();
        // Et token signeret med en anden nøgle (fx fra en anden installation) må ikke give adgang.
        client.DefaultRequestHeaders.Authorization = new("Bearer",
            "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJvaWQiOiJ4Iiwicm9sZXMiOiJFQS5BZG1pbiIsImlzcyI6ImVhLWRldiIsImF1ZCI6ImVhLWFwaSJ9.c2lnbmF0dXItZnJhLWVuLWFuZGVuLW5vZWdsZQ");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/systems")).StatusCode);
    }

    private static IEnumerable<Exception> Flatten(Exception ex)
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            yield return e;
            if (e is AggregateException agg)
            {
                foreach (var inner in agg.InnerExceptions.SelectMany(Flatten))
                {
                    yield return inner;
                }
            }
        }
    }
}
