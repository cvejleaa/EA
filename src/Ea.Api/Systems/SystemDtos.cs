using Ea.Api.Capabilities;
using Ea.Api.Persons;
using Ea.Api.Teams;

namespace Ea.Api.Systems;

public sealed record SystemRef(Guid Id, string Name);

public sealed record ModuleDto(Guid Id, string Name, LifecycleStatus LifecycleStatus);

public sealed record RoleAssignmentDto(SystemRole Role, PersonDto Person);

/// <summary>
/// Beregnet af serveren med de samme regler, som håndhæves ved skrivning. Klienten afgør aldrig selv adgang.
/// En begrundelse (ikke null) betyder, at handlingen er blokeret — og hvorfor.
/// </summary>
/// <param name="CanEditViaParent">
/// Brugeren kan redigere forælderen og dermed modulet — også uden en rolle på selve modulet (beslutning O). Formularen
/// advarer kun om "du kan ikke længere redigere", når det er falsk.
/// </param>
public sealed record SystemPermissions(
    bool CanEdit, bool CanDelete, string? DeleteBlockedReason, string? ParentBlockedReason, bool CanEditViaParent);

/// <summary>En, der kan redigere systemet: systemejer eller systemforvalter på systemet — eller på forælderen (Via).</summary>
public sealed record SystemEditor(PersonDto Person, SystemRef? Via);

public sealed record SystemDetail(
    Guid Id,
    string Name,
    IReadOnlyList<string> Aliases,
    string? Description,
    SystemType? Type,
    LifecycleStatus LifecycleStatus,
    TeamDto? ManagingTeam,
    SystemRef? Parent,
    IReadOnlyList<ModuleDto> Modules,
    IReadOnlyList<RoleAssignmentDto> Roles,
    IReadOnlyList<SystemEditor> Editors,
    IReadOnlyList<SystemCapabilityDto> Capabilities,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset LastConfirmedAt,
    string LastConfirmedByName,
    uint Version,
    SystemPermissions Permissions);

public sealed record SystemListItem(
    Guid Id,
    string Name,
    SystemRef? Parent,
    string? MatchedAlias,
    SystemType? Type,
    LifecycleStatus LifecycleStatus,
    TeamDto? ManagingTeam,
    PersonDto? BusinessOwner,
    DateTimeOffset LastConfirmedAt,
    int ModuleCount,
    IReadOnlyList<SystemRole>? MyRoles = null);

/// <param name="Total">Antal systemer i registret i alt (ufiltreret) — til "viser X af Y".</param>
/// <remarks>
/// Med <c>mine=true</c> har hver række <c>MyRoles</c>: brugerens egne roller på systemet (tom for et modul, der kun er
/// med via forælderen). Ellers er feltet null.
/// </remarks>
public sealed record SystemListResponse(IReadOnlyList<SystemListItem> Items, int Total);

public sealed record RoleAssignmentInput(SystemRole Role, Guid PersonId);

/// <summary>Oprettelse og redigering. <see cref="Version"/> kræves ved redigering (samtidighedstjek).</summary>
/// <param name="Roles">Den fulde liste af roller; null og tom betyder begge "ingen roller".</param>
/// <param name="CapabilityIds">
/// Systemets egne koblinger til kapabiliteter (hele listen). <b>null betyder uændret</b> — modsat <c>Roles</c> — så en
/// klient, der ikke kender feltet, aldrig sletter koblingerne tavst. En tom liste fjerner alle.
/// </param>
public sealed record SystemWriteRequest(
    string? Name,
    IReadOnlyList<string>? Aliases,
    string? Description,
    SystemType? Type,
    LifecycleStatus? LifecycleStatus,
    Guid? ManagingTeamId,
    Guid? ParentSystemId,
    IReadOnlyList<RoleAssignmentInput>? Roles,
    uint? Version,
    IReadOnlyList<Guid>? CapabilityIds = null);

public sealed record ConfirmSystemRequest(uint? Version);
