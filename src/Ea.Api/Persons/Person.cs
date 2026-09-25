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

    /// <summary>
    /// Værdien af claim "oid" for den, der logger ind som personen: Entra-objekt-id (spor A) — eller i den midlertidige
    /// drift et Firebase-id med præfikset "fb:". Det er nøglen, adgang bindes til — aldrig e-mail.
    /// </summary>
    public string? Oid { get; set; }
}
