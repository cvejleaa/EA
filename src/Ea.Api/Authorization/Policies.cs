namespace Ea.Api.Authorization;

/// <summary>
/// Alle adgangsbeslutninger træffes her i Authorization/ — og kun på serveren. Klienten viser/skjuler
/// knapper ud fra de permissions, serveren beregner med de samme policies.
/// </summary>
public static class Policies
{
    public const string CreateSystem = nameof(CreateSystem);
    public const string EditSystem = nameof(EditSystem);
    public const string ManagePersons = nameof(ManagePersons);
}
