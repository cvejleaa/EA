using Ea.Api.Common;
using Ea.Api.Systems;

namespace Ea.Api.Integrations;

/// <summary>
/// CSV-formatet for integrationer — eksporten i dag og importformatet i delopgave 6. Det er en EKSTERN kontrakt
/// (skabelonen til foranalysen): kolonner og deres rækkefølge ændres ikke. Spejles af docs/csv-integrationer.md
/// og docs/csv/integrationer-eksempel.csv.
/// </summary>
public static class IntegrationCsv
{
    public static readonly IReadOnlyList<string> Header =
    [
        "Id", "ExternalKey", "Navn", "DataFra", "DataTil", "Type", "ViaPlatform", "Dataobjekter", "Beskrivelse",
        "DataFraId", "DataTilId", "ViaPlatformId",
    ];

    /// <summary>Adskiller dataobjekter i ét felt.</summary>
    public const string DataObjectJoiner = " | ";

    /// <summary>Et moduls navn i CSV: "Forælder > Modul" (et tegn, man kan taste i Excel).</summary>
    public static string FullName(SystemEntity system) =>
        system.ParentSystem is null ? system.Name : $"{system.ParentSystem.Name} > {system.Name}";

    public static byte[] Write(IEnumerable<Integration> integrations)
    {
        var rows = integrations
            .Select(i => new
            {
                Integration = i,
                From = FullName(i.SourceSystem),
                To = FullName(i.TargetSystem),
            })
            .OrderBy(r => r.From, StringComparer.Ordinal)
            .ThenBy(r => r.To, StringComparer.Ordinal)
            .ThenBy(r => r.Integration.Type?.ToString() ?? "", StringComparer.Ordinal)
            .ThenBy(r => r.Integration.Name ?? "", StringComparer.Ordinal)
            .Select(r => (IReadOnlyList<string?>)
            [
                r.Integration.Id.ToString(),
                null, // ExternalKey: reserveret til importen (delopgave 6) — eksporteres tom.
                r.Integration.Name,
                r.From,
                r.To,
                r.Integration.Type?.ToString(),
                r.Integration.ViaPlatform is null ? null : FullName(r.Integration.ViaPlatform),
                string.Join(DataObjectJoiner, r.Integration.DataObjects
                    .Select(d => d.DataObject.Name)
                    .Order(StringComparer.Ordinal)),
                r.Integration.Description,
                r.Integration.SourceSystemId.ToString(),
                r.Integration.TargetSystemId.ToString(),
                r.Integration.ViaPlatformId?.ToString(),
            ]);

        return Csv.Write(Header, rows);
    }
}

/// <summary>Referenceliste over systemer — så den, der udfylder integrations-CSV'en, kan bruge de præcise navne.</summary>
public static class SystemCsv
{
    public static readonly IReadOnlyList<string> Header = ["Id", "FuldtNavn", "Navn", "Forælder", "Aliaser", "Type", "Status"];

    public static byte[] Write(IEnumerable<SystemEntity> systems) => Csv.Write(
        Header,
        systems
            .OrderBy(IntegrationCsv.FullName, StringComparer.Ordinal)
            .Select(s => (IReadOnlyList<string?>)
            [
                s.Id.ToString(),
                IntegrationCsv.FullName(s),
                s.Name,
                s.ParentSystem?.Name,
                string.Join(IntegrationCsv.DataObjectJoiner, s.Aliases),
                s.Type?.ToString(),
                s.LifecycleStatus.ToString(),
            ]));
}
