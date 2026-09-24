namespace Ea.Api.Integrations;

// Enum-navnene er en EKSTERN kontrakt (API, database som tekst, CSV-skabelonen til foranalysen).
// Omdøb dem aldrig — tilføj nye værdier i stedet. Danske visningsnavne ligger i klienten og i docs/csv-integrationer.md.

/// <summary>Hvordan data flyttes. DirekteDb er et brud på arkitekturprincip 3 (API First).</summary>
public enum IntegrationType
{
    Api,
    Fil,
    Event,
    DirekteDb,
    Udtraek,
}

/// <summary>
/// Det viste systems forhold til en integration (kun i svar — gemmes ikke). Retning er dataflow:
/// Ud = systemet sender data, Ind = systemet modtager data, Via = integrationen går gennem platformen,
/// Intern = begge ender er systemet selv eller dets moduler.
/// </summary>
public enum IntegrationRelation
{
    Ud,
    Via,
    Ind,
    Intern,
}
