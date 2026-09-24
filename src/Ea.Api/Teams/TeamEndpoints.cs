using Ea.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Ea.Api.Teams;

public sealed record TeamDto(Guid Id, string Name);

public static class TeamEndpoints
{
    public static void MapTeamEndpoints(this WebApplication app)
    {
        app.MapGet("/api/teams", async (EaDbContext db, CancellationToken ct) =>
                await db.Teams.AsNoTracking()
                    .OrderBy(t => t.Name)
                    .Select(t => new TeamDto(t.Id, t.Name))
                    .ToListAsync(ct))
            .WithTags("Teams");
    }
}
