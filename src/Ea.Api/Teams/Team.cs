namespace Ea.Api.Teams;

/// <summary>Et team, der forvalter systemer. Teams seedes i migrationen (ingen admin-flade endnu).</summary>
public sealed class Team
{
    public Guid Id { get; set; }

    public string Name { get; set; } = "";

    // Faste id'er, så seed-data og tests kan henvise til dem.
    public static readonly Guid Kerneapplikationer = new("0199a000-0000-7000-8000-000000000001");
    public static readonly Guid SpecialiseredeLoesninger = new("0199a000-0000-7000-8000-000000000002");
    public static readonly Guid DataOgIntegrationer = new("0199a000-0000-7000-8000-000000000003");
    public static readonly Guid DigitalArbejdsplads = new("0199a000-0000-7000-8000-000000000004");
    public static readonly Guid Stab = new("0199a000-0000-7000-8000-000000000005");

    internal static Team[] Seed() =>
    [
        new() { Id = Kerneapplikationer, Name = "Kerneapplikationer" },
        new() { Id = SpecialiseredeLoesninger, Name = "Specialiserede Løsninger" },
        new() { Id = DataOgIntegrationer, Name = "Data & Integrationer" },
        new() { Id = DigitalArbejdsplads, Name = "Digital Arbejdsplads" },
        new() { Id = Stab, Name = "Stab" },
    ];
}
