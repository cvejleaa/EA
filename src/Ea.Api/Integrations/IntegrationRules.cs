using Ea.Api.Systems;

namespace Ea.Api.Integrations;

/// <summary>Det, reglerne har brug for at vide om et system (ende eller platform).</summary>
public sealed record SystemInfo(Guid Id, string Name, SystemType? Type);

/// <summary>
/// Forretningsreglerne for integrationer — samlet ét sted og brugt af både endpoints og tests.
/// </summary>
public static class IntegrationRules
{
    public const int NameMaxLength = 200;
    public const int DescriptionMaxLength = 4000;
    public const int DataObjectMaxCount = 50;

    /// <summary>Adskiller dataobjekter i CSV-formatet — derfor forbudt i dataobjekters navne.</summary>
    public const char DataObjectSeparator = '|';

    /// <summary>
    /// Validerer en integration. <paramref name="viaChanged"/>: platformens type tjekkes kun, når via ændres —
    /// ellers bliver en eksisterende integration umulig at redigere, hvis platformen senere skifter type.
    /// </summary>
    public static Dictionary<string, string[]> Validate(
        Guid? fromId,
        SystemInfo? from,
        Guid? toId,
        SystemInfo? to,
        Guid? viaId,
        SystemInfo? via,
        bool viaChanged,
        string? name,
        string? description,
        int dataObjectCount)
    {
        var errors = new Dictionary<string, string[]>();

        if (fromId is null)
        {
            errors["fromSystemId"] = ["Vælg det system, data sendes fra."];
        }
        else if (from is null)
        {
            errors["fromSystemId"] = ["Systemet, data sendes fra, findes ikke."];
        }

        if (toId is null)
        {
            errors["toSystemId"] = ["Vælg det system, data sendes til."];
        }
        else if (to is null)
        {
            errors["toSystemId"] = ["Systemet, data sendes til, findes ikke."];
        }
        else if (toId == fromId)
        {
            errors["toSystemId"] = ["En integration kan ikke gå fra et system til sig selv."];
        }

        if (viaId is not null)
        {
            if (viaId == fromId || viaId == toId)
            {
                errors["viaPlatformId"] = ["Platformen kan ikke også være en af enderne."];
            }
            else if (viaChanged && via is null)
            {
                errors["viaPlatformId"] = ["Den valgte platform findes ikke."];
            }
            else if (viaChanged && via!.Type != SystemType.Platform)
            {
                errors["viaPlatformId"] = [$"{via.Name} er ikke registreret som platform (systemtype Platform)."];
            }
        }

        if (name is { Length: > NameMaxLength })
        {
            errors["name"] = [$"Navnet må højst være {NameMaxLength} tegn."];
        }

        if (description is { Length: > DescriptionMaxLength })
        {
            errors["description"] = [$"Beskrivelsen må højst være {DescriptionMaxLength} tegn."];
        }

        if (dataObjectCount > DataObjectMaxCount)
        {
            errors["dataObjectIds"] = [$"Højst {DataObjectMaxCount} dataobjekter pr. integration."];
        }

        return errors;
    }

    public static string? ValidateDataObjectName(string? name)
    {
        var trimmed = name?.Trim() ?? "";
        if (trimmed.Length == 0)
        {
            return "Navn skal udfyldes.";
        }

        if (trimmed.Length > NameMaxLength)
        {
            return $"Navnet må højst være {NameMaxLength} tegn.";
        }

        return trimmed.Contains(DataObjectSeparator, StringComparison.Ordinal)
            ? $"Navnet må ikke indeholde tegnet '{DataObjectSeparator}'."
            : null;
    }

    /// <summary>
    /// Det viste systems forhold til en integration. <paramref name="family"/> er systemet selv plus dets moduler,
    /// så modulers integrationer rulles op på forælderen. Null: integrationen hører ikke til systemet.
    /// </summary>
    public static IntegrationRelation? RelationTo(IReadOnlySet<Guid> family, Guid sourceId, Guid targetId, Guid? viaId)
    {
        var source = family.Contains(sourceId);
        var target = family.Contains(targetId);

        if (source && target)
        {
            return IntegrationRelation.Intern;
        }

        if (source)
        {
            return IntegrationRelation.Ud;
        }

        if (target)
        {
            return IntegrationRelation.Ind;
        }

        return viaId is { } via && family.Contains(via) ? IntegrationRelation.Via : null;
    }

    /// <summary>Beskeden ved en dublet — peger på den eksisterende integration og på vejen videre.</summary>
    public static string DuplicateMessage(string fromName, string toName, IntegrationType? type, string? viaName, string? name)
    {
        var what = type is null ? "en integration uden angivet type" : $"en {TypeLabel(type.Value)}-integration";
        var via = viaName is null ? "uden platform" : $"via {viaName}";
        var named = name is null ? "uden navn" : $"med navnet \"{name}\"";
        return $"Der findes allerede {what} fra {fromName} til {toName} {via} og {named}. " +
               "Tilføj dataobjekterne til den, eller giv den nye integration et navn, der skelner den.";
    }

    public static string TypeLabel(IntegrationType type) => type switch
    {
        IntegrationType.Api => "API",
        IntegrationType.Fil => "fil",
        IntegrationType.Event => "event",
        IntegrationType.DirekteDb => "direkte database",
        IntegrationType.Udtraek => "udtræk",
        _ => type.ToString(),
    };
}
