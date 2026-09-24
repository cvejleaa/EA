# Arkitekt — genbrugskatalog og faste fund

## Projekt (pr. 2026-09-24)
EA-værktøj/applikationsregister for ITFL/DTU ("LeanIX-light"). Prototype m. FIKTIVE data i personligt repo.
Stack: .NET 10 + EF Core/Npgsql + Angular + PostgreSQL 16. Senere DTU Azure (PG Flexible Server) + Entra.
Første plan lagt 2026-09-24: 1 fundament+systemregister, 2 integrationer+"hvad rammes", 3 kapabiliteter+overlap,
4 forvaltere+audit, 5 teknologi+EOL, 6 import, 7 processer+SOP, 8 kontrakter. Azure/Entra = separat spor efter 2.

## Miljø-fakta (verificeret)
- dotnet SDK 10.0.112; node 22.22.2 (/opt/node22) — FOR GAMMEL til Angular 22 (kræver ^22.22.3 || ^24.15). nvm i /opt/nvm, nodejs.org nåbar.
- PostgreSQL 16 lokalt, cluster 16/main (start: pg_ctlcluster 16 main start). Docker-klient findes, men INGEN daemon → ingen Testcontainers.
- nuget.org + registry.npmjs.org nåbare. endoflife.date, caudit.edu.au, ucisa.ac.uk IKKE nåbare (proxy 403).
- xunit.v3 4.x trækker Microsoft.Testing.Platform (mtp-v2) → global.json "test": {"runner": "Microsoft.Testing.Platform"}.

## Platform-katalog (brug, byg ikke)
- Adgang: ASP.NET Core policies + resource-based AuthorizationHandler; FallbackPolicy = authenticated. Globale roller = Entra app roles i `roles`-claim.
- Auth: JwtBearer i begge modes (dev-token vs Entra/Microsoft.Identity.Web). MapInboundClaims=false; claims: oid, name, roles.
- OpenAPI: Microsoft.AspNetCore.OpenApi (ikke Swashbuckle). TS-typer: openapi-typescript (kun typer).
- Validering: .NET 10 AddValidation() (ikke FluentValidation). Fejl: AddProblemDetails.
- Tid: TimeProvider + FakeTimeProvider (ikke egen IClock). Id: Guid.CreateVersion7().
- Concurrency: Npgsql xmin via IsRowVersion(). Audit: EF SaveChangesInterceptor (temporal tables er SQL Server-only).
- Testisolation: CREATE DATABASE x TEMPLATE <migreret skabelon> (ikke Respawn/Testcontainers).
- Case-insensitiv unik: stored generated column lower(name) + unik index (nondeterministic collation understøtter ikke LIKE før PG18; citext kræver Azure-allowlist).
- Angular: Material/CDK, funktionelle interceptors, signals, Vitest via @angular/build:unit-test, angular-eslint.
- Undgå (licens): AutoMapper, MediatR, FluentAssertions v8 (kommercielle siden 2025). Moq fravalgt.

## Genbrugskatalog — kode efter delopgave 1 (commit 100b074)
- Unik navn case-insensitiv: SystemEntity.Name-setter sætter NameNormalized (lowerInvariant) + unikt indeks
  (EaDbContext.cs:42-45, AreNullsDistinct(false) = PG "NULLS NOT DISTINCT"). Genbrug til dataobjekter/naturlige nøgler.
- 409 på unik-brud: SystemEndpoints.Save (l.442) fanger PostgresException UniqueViolation + ConstraintName.
- Samtidighed: xmin Version + OriginalValue-trick (SystemEndpoints UpdateSystem/Confirm). Problems.StaleVersion() siger "Systemet" hårdt.
- Slet-blokering: SystemRules.DeleteBlockedReason (SystemRules.cs:23) bruges af DeleteSystem (l.311) OG ToDetail (l.475) → permissions.
- Kandidat-endpoint med samme regel som gem: /api/systems/parent-candidates (l.145). Systemliste har ?type=&q= (søg navn/alias/forælder).
- Rolle-diff (RemoveAll + Add) ved M:N-opdatering: SystemEndpoints.cs:419. Mønster for join-tabeller.
- Opret-inline-mønster: PersonEndpoints (GET ?q + POST m. policy ManagePersons) + formularens "Personen findes ikke på listen?" (system-form.page.html:121-137).
- Policies registreres i Auth/AuthSetup.cs:69-79 (ikke i Authorization/). MePermissions i CurrentUser/CurrentUserEndpoints.cs:8.
- Kontrakt-golden-file-mønster: tests/.../Contract/OpenApiContractTests.cs (UPDATE_CONTRACT=1, privat RepoRoot()).
- Anonym-401-løkke over alle endpoints: AuthorizationTests.cs:48-69 (erstatter kun "{id:guid}"; tæller >= 9).
- DevSeed returnerer tidligt hvis der findes systemer → nye seed-typer skal have egen vagt, ellers får eksisterende dev-DB'er dem aldrig.
- Web: labels.ts Record<Enum,string>; systems.api.ts; testing/fixtures.ts (systemDetail/listItem/me/settle); toProblem (core/problem.ts).
- TextFieldParser (Microsoft.VisualBasic.FileIO) findes i net10 ref-pack — uafhængig CSV-parser til tests/import (ingen NuGet).

## Delopgave 2-plan (2026-09-24) — mine beslutninger (se rapporten for begrundelse)
- Retning = fra/til som DATAFLOW; intet retningsfelt, tovejs = to rækker. fra/til uforanderlige efter oprettelse.
- Naturlig nøgle (fra, til, type, via) NULLS NOT DISTINCT fanger "registreret fra begge ender".
- Ingen status på integration (afledes af endernes livscyklus); hård sletning, historik via audit i delopg. 4.
- CSV: ;-separeret, UTF-8 m. BOM, CRLF, Id + ExternalKey-kolonne reserveret (tom), golden-fil i docs.

## Genbrugskatalog — kode efter delopgave 2 (main 1434f77)
- Common/Csv.cs: KUN skrivning (Write l.21, Field l.38, formelvagt FormulaStart l.18 + AfterOtherSeparator l.72-74, Slug l.77).
  Ingen læser i prod-kode. Import skal fjerne formel-apostroffen igen (lovet i docs/csv-integrationer.md "Import: hvad sker der").
- Common/Problems.cs: StaleVersion/Duplicate/Blocked (typede 409, l.14-28), Validation(dict) l.30, DbErrors.IsUniqueViolation l.43.
- Systems/SystemEndpoints.cs: None="none"-filterkonvention l.17-18/55-79; parent-candidates l.153-171 (kandidater via SAMME regel som gem);
  Update tvinger rækkeskrivning (Touch + IsModified, l.249-253) → xmin dækker også rene relationsændringer; rolle-diff l.431-440;
  ToDetail→permissions l.486-515. Roller er cascade (EaDbContext l.67-70).
- IntegrationQueries.FamilyOf (l.22) = system + moduler; IntegrationRules.Validate "tjek kun ved ændring" (viaChanged, l.56-58/95-108).
- IntegrationCsv.FullName ("Forælder > Modul", l.23), SystemCsv (l.62); golden+docs-test: CsvTemplateTests.cs l.78-103.
- Resource-niveau permission i response: SystemIntegrationsResponse.CanAdd (IntegrationEndpoints l.68-71). MePermissions i CurrentUserEndpoints l.8.
- SystemLink DTO (IntegrationDtos l.6) = id,name,parent,type,status — genbrug når systemer listes under noget andet.
- Web: core/download.ts downloadFile (l.8), labels.ts count()/systemDisplayName, system-integrations.component.ts flags() l.81-94
  (klient-afledte markeringer — OK til visning, IKKE til regler). Ingen fil-upload findes i web endnu. Toolbar (app.ts l.11-18) har ingen nav.
- AuthorizationTests: allow-liste l.17-24, anonym-401-løkke kræver >= 20 endpoints (l.58).
- Minimal API + IFormFile kræver .DisableAntiforgery() (ellers fejl uden UseAntiforgery) — bearer-only, ingen cookies.

## Delopgave 3-plan (2026-09-24) — mine beslutninger
- Kapabilitet: Kode (naturlig nøgle, case-insens. unik), Navn, Beskrivelse, ParentId (adjacency), RetiredAt. Import = HELE modellen
  (modsat integrations-CSV). Mangler i fil: uden koblinger → slettes; med koblinger → udgået (løsrives fra træet), genopstår ved samme kode.
- Kobling på SystemWriteRequest.CapabilityIds (null = uændret!) → genbruger EditSystem, Touch/xmin, "Bekræft uændret". Kun blade, ikke udgåede
  (tjekkes kun for NYE koblinger). Ingen felter på koblingen.
- Overlap: CapabilityRules.Overlap, familienøgle ParentSystemId ?? Id; default: tæller Indfases+IDrift, ikke Udfases/Planlagt/Nedlagt/LokalLoesning.
- Import: ét endpoint dryRun + fingerprint (409 stale ved afvigelse). Csv.Read i Common (TextFieldParser + strikt UTF-8 + formelvagt-invers).
