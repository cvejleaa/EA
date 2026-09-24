namespace Ea.Api.Persons;

/// <summary>
/// En person, der kan have en rolle på et system. I produktion er det en cache af Entra ID
/// (kilden); i prototypen oprettes fiktive personer manuelt.
/// </summary>
public sealed class Person
{
    public Guid Id { get; set; }

    public string DisplayName { get; set; } = "";

    public string? Email { get; set; }

    /// <summary>Afdeling — giver forretningsvinklen på et system via dets forretningsejer.</summary>
    public string? Department { get; set; }

    /// <summary>Entra-objekt-id (claim "oid"). Nøglen, som adgang senere bindes til — aldrig e-mail.</summary>
    public string? EntraObjectId { get; set; }
}
