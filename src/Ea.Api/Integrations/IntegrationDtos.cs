using Ea.Api.Systems;

namespace Ea.Api.Integrations;

/// <summary>Et system set fra en integration: navn, evt. forælder ("Forælder › Modul"), type og livscyklus.</summary>
public sealed record SystemLink(Guid Id, string Name, SystemRef? Parent, SystemType? Type, LifecycleStatus LifecycleStatus)
{
    public static SystemLink From(SystemEntity s) => new(
        s.Id,
        s.Name,
        s.ParentSystem is null ? null : new SystemRef(s.ParentSystem.Id, s.ParentSystem.Name),
        s.Type,
        s.LifecycleStatus);
}

public sealed record DataObjectDto(Guid Id, string Name);

/// <summary>Beregnet af serveren (EditIntegration). Sletning kræver det samme som redigering.</summary>
public sealed record IntegrationPermissions(bool CanEdit);

public sealed record IntegrationDto(
    Guid Id,
    string? Name,
    SystemLink From,
    SystemLink To,
    SystemLink? Via,
    IntegrationType? Type,
    string? Description,
    IReadOnlyList<DataObjectDto> DataObjects,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    uint Version,
    IntegrationPermissions Permissions);

/// <param name="Counterpart">Systemet i den anden ende (Ud: modtageren, Ind: afsenderen). Null for Via og Intern.</param>
/// <param name="LocalModule">Modulet, integrationen er rullet op fra, når den ikke hænger direkte på systemet.</param>
public sealed record SystemIntegrationItem(
    IntegrationRelation Relation,
    SystemLink? Counterpart,
    SystemRef? LocalModule,
    IntegrationDto Integration);

/// <summary>
/// "Hvad hænger på systemet" i tal. Tæller forskellige SYSTEMER (ikke rækker) for modtagere/leverandører, så to
/// integrationer til samme system ikke overdriver svaret.
/// </summary>
/// <param name="Receivers">Forskellige systemer, der modtager data fra systemet (eller dets moduler).</param>
/// <param name="Suppliers">Forskellige systemer, systemet modtager data fra.</param>
/// <param name="ViaPlatform">Integrationer mellem andre systemer, der går via dette system som platform.</param>
/// <param name="LocalSolutions">Heraf forskellige modtagere/leverandører af typen "Lokal løsning/udtræk".</param>
/// <param name="DirectDb">Integrationer med direkte databaseadgang (brud på API First).</param>
public sealed record IntegrationSummary(int Receivers, int Suppliers, int ViaPlatform, int LocalSolutions, int DirectDb);

public sealed record SystemIntegrationsResponse(
    IntegrationSummary Summary,
    IReadOnlyList<SystemIntegrationItem> Items,
    bool CanAdd);

public sealed record IntegrationCreateRequest(
    Guid? FromSystemId,
    Guid? ToSystemId,
    Guid? ViaPlatformId,
    IntegrationType? Type,
    string? Name,
    string? Description,
    IReadOnlyList<Guid>? DataObjectIds);

/// <summary>Fra og til kan ikke ændres — slet og opret en ny integration, hvis en ende er forkert.</summary>
public sealed record IntegrationUpdateRequest(
    Guid? ViaPlatformId,
    IntegrationType? Type,
    string? Name,
    string? Description,
    IReadOnlyList<Guid>? DataObjectIds,
    uint? Version);

public sealed record CreateDataObjectRequest(string? Name);
