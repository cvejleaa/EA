using System.Security.Claims;
using Ea.Api.Auth;
using Ea.Api.Data;
using Ea.Api.Systems;
using Microsoft.EntityFrameworkCore;

namespace Ea.Api.Authorization;

/// <summary>
/// Hvilke systemer brugeren må redigere — slået op ÉN gang pr. request, så lister og permissions ikke laver et
/// opslag pr. række. Reglen: brugerens person (bundet via claim "oid") har en redigerende rolle
/// (<see cref="SystemRules.EditingRoles"/>) på systemet eller på dets forælder. En rolle på et modul giver ikke ret
/// over forælderen. Enterprise arkitekten må alt og slår ikke op.
/// </summary>
public sealed class SystemAccess(EaDbContext db)
{
    private IReadOnlySet<Guid>? _editable;

    public async Task<bool> CanEditAsync(ClaimsPrincipal user, Guid systemId, CancellationToken ct = default) =>
        user.IsInRole(AppRoles.Admin) || (await EditableAsync(user, ct)).Contains(systemId);

    /// <summary>De systemer (inkl. moduler), brugeren har en redigerende rolle på. Tom for en bruger uden person.</summary>
    public async Task<IReadOnlySet<Guid>> EditableAsync(ClaimsPrincipal user, CancellationToken ct = default)
    {
        if (_editable is not null)
        {
            return _editable;
        }

        var oid = user.FindFirstValue(ClaimNames.ObjectId);
        if (oid is null)
        {
            return _editable = new HashSet<Guid>();
        }

        var roles = SystemRules.EditingRoles;
        var direct = await db.SystemRoles.AsNoTracking()
            .Where(r => r.Person.Oid == oid && roles.Contains(r.Role))
            .Select(r => r.SystemId)
            .Distinct()
            .ToListAsync(ct);

        var modules = await db.Systems.AsNoTracking()
            .Where(s => s.ParentSystemId != null && direct.Contains(s.ParentSystemId.Value))
            .Select(s => s.Id)
            .ToListAsync(ct);

        return _editable = direct.Concat(modules).ToHashSet();
    }

    /// <summary>Kaldes efter en gemning, der kan have ændret roller eller forælder, så svaret viser de nye rettigheder.</summary>
    public void Invalidate() => _editable = null;
}
