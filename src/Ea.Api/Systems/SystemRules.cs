namespace Ea.Api.Systems;

/// <summary>Hvad afhænger af et system: moduler, integrationer (som ende) og integrationer via det (som platform).</summary>
public sealed record SystemUsage(int Modules, int Integrations, int PlatformFor);

/// <summary>Minimal oplysning om et muligt forældersystem.</summary>
public sealed record ParentInfo(Guid Id, string Name, Guid? ParentSystemId);

/// <summary>
/// Forretningsreglerne for systemer — samlet ét sted. Både endpoints og de permissions, klienten får,
/// bruger disse funktioner, så en knap aldrig lover noget, serveren afviser.
/// </summary>
public static class SystemRules
{
    public const int NameMaxLength = 200;
    public const int DescriptionMaxLength = 4000;
    public const int AliasMaxCount = 20;

    /// <summary>Roller, som højst én person kan have pr. system (entydigt ejerskab).</summary>
    public static readonly IReadOnlySet<SystemRole> SingleHolderRoles =
        new HashSet<SystemRole> { SystemRole.Forretningsejer, SystemRole.Systemejer };

    /// <summary>
    /// Nøglen, overlap samler på: et system og dets moduler (også to søskendemoduler) er ét system. Dækning bruger
    /// en anden regel — se <c>CapabilityQueries.Missing</c>.
    /// </summary>
    public static Guid OverlapGroupKey(Guid systemId, Guid? parentSystemId) => parentSystemId ?? systemId;

    /// <summary>
    /// Rollerne, der giver ret til at redigere systemet og dets moduler (beslutning 13: forvalterne vedligeholder;
    /// forretningsejeren ejer processen, men redigerer ikke). Enterprise arkitekten må alt.
    /// </summary>
    public static readonly IReadOnlyList<SystemRole> EditingRoles = [SystemRole.Systemejer, SystemRole.Systemforvalter];

    /// <summary>Sletning er til fejloprettelser og kun for enterprise arkitekten; forvalteren skal vide, hvad hun gør i stedet.</summary>
    public const string DeleteRequiresAdminReason =
        "Kun enterprise arkitekten kan slette systemer. Er systemet taget ud af brug, så sæt status til Nedlagt.";

    public static string? ParentChangeBlockedReason(int moduleCount) =>
        moduleCount > 0 ? "Systemet har selv moduler og kan derfor ikke gøres til modul." : null;

    /// <summary>Et modul tages kun ud af sin forælder af den, der må redigere forælderen (dens dækning og overlap ændres).</summary>
    public static string ParentMoveBlockedReason(string parentName) =>
        $"Modulet kan kun flyttes af den, der kan redigere {parentName}.";

    /// <summary>
    /// Et system, der er i brug, kan ikke slettes — sletning er til fejloprettelser og må ikke fjerne historik.
    /// Samme funktion bruges ved sletning og i de permissions, klienten viser knappen ud fra.
    /// </summary>
    public static string? DeleteBlockedReason(string name, SystemUsage usage)
    {
        var parts = new List<string>();
        if (usage.Modules > 0)
        {
            parts.Add($"har moduler ({usage.Modules})");
        }

        if (usage.Integrations > 0)
        {
            parts.Add($"indgår i integrationer ({usage.Integrations})");
        }

        if (usage.PlatformFor > 0)
        {
            parts.Add($"er platform for integrationer ({usage.PlatformFor})");
        }

        return parts.Count == 0
            ? null
            : $"{name} {string.Join(" og ", parts)} — flyt eller slet dem først, eller sæt status til Nedlagt.";
    }

    /// <summary>Moduler ligger præcis ét niveau under et system.</summary>
    public static string? ValidateParent(Guid? systemId, int moduleCount, Guid? proposedParentId, ParentInfo? proposedParent)
    {
        if (proposedParentId is null)
        {
            return null;
        }

        if (proposedParentId == systemId)
        {
            return "Et system kan ikke være modul af sig selv.";
        }

        if (proposedParent is null)
        {
            return "Det valgte forældersystem findes ikke.";
        }

        if (proposedParent.ParentSystemId is not null)
        {
            return $"{proposedParent.Name} er selv et modul. Moduler kan kun ligge ét niveau under et system.";
        }

        return ParentChangeBlockedReason(moduleCount);
    }

    /// <summary>Samme person kan ikke have samme rolle to gange, og ejerroller har højst én indehaver.</summary>
    public static string? ValidateRoles(IReadOnlyList<RoleAssignmentInput> roles)
    {
        if (roles.Select(r => (r.Role, r.PersonId)).Distinct().Count() != roles.Count)
        {
            return "Samme person har samme rolle flere gange.";
        }

        var duplicate = roles
            .Where(r => SingleHolderRoles.Contains(r.Role))
            .GroupBy(r => r.Role)
            .FirstOrDefault(g => g.Count() > 1);

        return duplicate is null ? null : $"Et system kan kun have én {RoleLabel(duplicate.Key)}.";
    }

    public static string RoleLabel(SystemRole role) => role switch
    {
        SystemRole.Forretningsejer => "forretningsejer",
        SystemRole.Systemejer => "systemejer",
        SystemRole.Systemforvalter => "systemforvalter",
        _ => role.ToString(),
    };

    /// <summary>Trimmer aliaser og fjerner tomme, dubletter og aliaser magen til navnet.</summary>
    public static List<string> NormalizeAliases(IEnumerable<string>? aliases, string name) =>
        (aliases ?? [])
            .Select(a => a.Trim())
            .Where(a => a.Length > 0 && !string.Equals(a, name.Trim(), StringComparison.OrdinalIgnoreCase))
            .DistinctBy(a => a.ToLowerInvariant())
            .ToList();
}
