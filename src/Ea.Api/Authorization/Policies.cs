namespace Ea.Api.Authorization;

/// <summary>
/// Alle adgangsbeslutninger træffes her i Authorization/ — og kun på serveren. Klienten viser/skjuler
/// knapper ud fra de permissions, serveren beregner med de samme policies.
/// </summary>
public static class Policies
{
    public const string CreateSystem = nameof(CreateSystem);
    public const string EditSystem = nameof(EditSystem);

    /// <summary>Skift af forælder: kræver ret over systemet, den gamle og den nye forælder (resurse: <see cref="ParentChange"/>).</summary>
    public const string MoveSystem = nameof(MoveSystem);

    /// <summary>Sletning er til fejloprettelser — kun enterprise arkitekten. Tjekkes FØR låsen, uden resurse.</summary>
    public const string DeleteSystem = nameof(DeleteSystem);
    public const string ManagePersons = nameof(ManagePersons);
    public const string EditIntegration = nameof(EditIntegration);
    public const string ManageDataObjects = nameof(ManageDataObjects);

    /// <summary>Importere kapabilitetskortet (HELE kortet erstattes) — kun enterprise arkitekten.</summary>
    public const string ManageCapabilities = nameof(ManageCapabilities);
}
