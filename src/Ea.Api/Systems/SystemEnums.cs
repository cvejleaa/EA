namespace Ea.Api.Systems;

// Enum-navnene er en EKSTERN kontrakt: de står i API'et, i databasen (som tekst) og i den kommende
// CSV-skabelon. Omdøb dem aldrig — tilføj nye værdier i stedet. Danske visningsnavne ligger i klienten.

/// <summary>Livscyklus. "Aktiv" (i brug) er Indfases, IDrift og Udfases.</summary>
public enum LifecycleStatus
{
    Planlagt,
    Indfases,
    IDrift,
    Udfases,
    Nedlagt,
}

/// <summary>Systemtype. Om noget er et modul, følger af forælderen — derfor ingen "Modul"-type.</summary>
public enum SystemType
{
    Saas,
    Standardsystem,
    Egenudviklet,
    LowCode,
    Platform,
    LokalLoesning,
}

/// <summary>Personroller på et system. Forretningsejer = den i forretningen, der ejer processen, systemet understøtter.</summary>
public enum SystemRole
{
    Forretningsejer,
    Systemejer,
    Systemforvalter,
}
