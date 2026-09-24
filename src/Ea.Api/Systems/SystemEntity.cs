using Ea.Api.Capabilities;
using Ea.Api.Teams;

namespace Ea.Api.Systems;

/// <summary>
/// Et system i registret. Et modul er et system med <see cref="ParentSystemId"/> sat (ét niveau).
/// </summary>
public sealed class SystemEntity
{
    private string _name = "";

    public Guid Id { get; set; }

    /// <summary>Trimmes ved tildeling; <see cref="NameNormalized"/> følger med, så unikhed ikke afhænger af databasens locale.</summary>
    public string Name
    {
        get => _name;
        set
        {
            _name = value.Trim();
            NameNormalized = _name.ToLowerInvariant();
        }
    }

    public string NameNormalized { get; private set; } = "";

    public List<string> Aliases { get; set; } = [];

    public string? Description { get; set; }

    public SystemType? Type { get; set; }

    public LifecycleStatus LifecycleStatus { get; set; }

    public Guid? ManagingTeamId { get; set; }

    public Team? ManagingTeam { get; set; }

    public Guid? ParentSystemId { get; set; }

    public SystemEntity? ParentSystem { get; set; }

    public List<SystemEntity> Modules { get; set; } = [];

    public List<SystemRoleAssignment> Roles { get; set; } = [];

    /// <summary>Kapabiliteter, systemet selv er koblet til (modulers koblinger ligger på modulerne).</summary>
    public List<SystemCapability> CapabilityLinks { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Sidste ændring af systemets data (ikke en ren bekræftelse).</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Sidst nogen har bekræftet, at oplysningerne er rigtige — ved oprettelse, redigering eller "Bekræft uændret".</summary>
    public DateTimeOffset LastConfirmedAt { get; set; }

    public string LastConfirmedByOid { get; set; } = "";

    public string LastConfirmedByName { get; set; } = "";

    /// <summary>PostgreSQL xmin — samtidighedstjek ved skrivning.</summary>
    public uint Version { get; set; }
}
