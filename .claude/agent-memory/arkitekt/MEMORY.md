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

## Genbrugskatalog — kode efter 3b-web (main 391e82b)
- FINDES IKKE (trods antagelser): SystemRules.FamilyKey, OverlapExclusion, noget overlap-kode, koblings-CSV.
- Common/CsvRead.cs: Csv.Read (l.27, strikt UTF-8, Unguard), ImportRowError (l.9), SaveAsUtf8Advice (l.17).
- CapabilityImport.cs: Parse (l.26, header-tjek m. komma-hint l.39-46), Plan (l.210), Fingerprint = SHA256(JSON(Changes)) (l.325),
  Apply (l.328), ReadBodyAsync (l.389, GENERISK — hører i Common), WithoutTrailingEmpty (l.375), MaxErrors=200 (l.24).
- CapabilityEndpoints.Import (l.72-137): tør-kørsel/fingeraftryk-protokol; commit i ExecutionStrategy + tx +
  "LOCK TABLE ea.capabilities, ea.system_capabilities IN SHARE ROW EXCLUSIVE MODE" (l.111) → genberegn → sammenlign → StaleDryRun.
- Problems.StaleDryRun() (l.31) har HÅRDKODET tekst om "Kortet" → skal parametriseres ved næste import.
- CapabilityRules: CoupleBlockedReason (l.31, selectable-reglen), MoveReasonOf (l.42), MaxCouplingsPerSystem=100 (l.16),
  MaxRows=5000/MaxFileBytes=2MB (l.18-19, for kortet), CodeOrder, Ordered, PathOf/DisplayPath.
- CapabilityQueries.CoupledSystemsAsync (l.9): CoupledSystem(Id,"Forælder › Modul") pr. kapabilitet — indgår i import-fingeraftryk
  (AffectedSystems) → udvid ikke CoupledSystem; lav en rigere holder-query og projicér.
- SystemEndpoints: capabilityId=none-prædikat (l.83-92; forælder dækket af moduler, modul af forælder, IKKE søskende),
  WithCouplingLock (l.506, ROW EXCLUSIVE på system_capabilities før validering), ValidateCapabilities (l.531: loft på RÅ liste,
  kun NYE valideres), koblings-diff (l.486-495), CapabilitiesOf (l.611, familie-dict), Touch (l.573 sætter OGSÅ LastConfirmed).
- Deterministisk låse-race-testmønster: CouplingTests.WaitForBlockedLockAsync/Sql (l.407-430) — flyt til Infrastructure ved genbrug.
- Web: capability-import.page.ts (tilstandsmaskine file/result/done/problem/busy/canCommit — TM fandt 4 subtile huller her → generalisér,
  kopiér ikke), capabilities.api.ts import() (l.24), system-list setFilter→URL (l.122-131), labels.ts Records.
- EnableRetryOnFailure (DatabaseSetup l.27) → 40P01/40001 genkøres af ExecutionStrategy.

## Plan 3c/3d (2026-09-24) — mine beslutninger
- Skæring: 3c-1 (regel+koblings-CSV+knap) → 3c-2 (API-felter) → 3d-server → 3d-web (ImportFlow generaliseret) → 3c-web.
- Undtagelses-forrang: Nedlagt > Udfases > LokalLoesning > Planlagt (Planlagt sidst, så kun "rigtige" planlagte giver mærket).
- Overlap kun på valgbare kapabiliteter (MoveReason != null → ingen vurdering). Familie = ParentSystemId ?? Id (inkl. søskende)
  ≠ dækning (selv+forælder+moduler, ikke søskende). Holderens egen status/type afgør.
- Koblings-CSV: én række pr. egen kobling + én tom-Kode-række pr. system uden egne koblinger (= arbejdsliste OG "fjern alle").
- 3d: fil = fuldt sæt egne koblinger for systemer i filen; kun NYE valideres; systems-rækken bumpes (UpdatedAt, ikke LastConfirmed).

## Genbrugskatalog — adgang/identitet efter 3c-web (main a517cc0)
- EditSystemHandler/EditIntegrationHandler (Authorization/) er SINGLETONS og ser kun IsInRole(Admin); resursen ignoreres.
  CreateIntegration autoriserer en IKKE-gemt Integration med kun id'er (ingen navigationer) → handler skal slå op på id.
  CanAdd = fake Integration{Source=id,Target=id} (IntegrationEndpoints l.68). ToDto pr. integration → N+1 hvis handler querier.
- Person.EntraObjectId (Person.cs l.19, unikt indeks EaDbContext l.107-108) findes; POST /api/persons kan IKKE sætte den.
  DevSeed.FridaOid (l.19) = dev-bruger "frida" = systemforvalter på Nordlys ERP + Laborant (forberedt til delopg. 4).
- Person.Email: ikke normaliseret, ikke unik. Ingen person-slet/-redigér-endpoints.
- /api/auth/mode (DevAuthEndpoints l.21) = klientens mode-switch; AuthService-kommentar forudser udskiftning af login-servicen.
  Klienten: getToken() er SYNKRON (sessionStorage) → async token (Firebase/MSAL) kræver generalisering af interceptor.
- Klienten følger allerede permissions (canEdit/canDelete/canAdd, parentBlockedReason) — ingen klient-adgangslogik at fjerne.
- ParentCandidates (SystemEndpoints l.169) = kandidater via SAMME regel som gem → skal også filtreres på målret (inkl. nuv. forælder).
- Alle skrivninger går via ChangeTracker (ingen ExecuteUpdate/Delete) → SaveChangesInterceptor fanger alt. MEN cascade-slettede
  børn (roller, koblinger, integration_data_objects) er ikke tracked, medmindre slet-endpoint indlæser dem.
- Ingen Dockerfile, intet deploy, ingen E2E. Playwright-browsere i /opt/pw-browsers (chromium-1194), @playwright/test ikke installeret.
- AuthorizationTests: Production+Dev kaster (l.159), Entra kaster NotSupported (l.167).

## Plan delopg. 4 + Firebase-drift (2026-09-25) — mine anbefalinger
- Skiver: 4a server-adgang(+rename EntraObjectId→Oid, forældreskift, slet=admin) → 4b "Mine systemer"+E2E → F0 spike (Firebase-claims,
  præ-kapring) → F1 server Firebase-mode → F2 web e-mail-link → F3 container/firebase.json/CLI-modes → [ejertrin] → F4 deploy.yml → 4c
  historik server (interceptor + ea.change_log, system_ids uuid[]) → 4d historik web. Schema-omdøbninger FØR første prod-deploy.
- oid i Firebase-mode = "fb:"+sub (ren funktion af tokenet); Person.Oid bindes ÉN gang via verificeret e-mail (betinget UPDATE WHERE oid IS NULL).
  Admin via config-liste af oid'er (ikke e-mail). Ikke-registreret → 403 urn:ea:problem:not-registered (én vagt i OnTokenValidated/OnChallenge).
- Adgangssæt pr. request (scoped): systemer hvor oid har Systemejer/Systemforvalter + deres moduler. Ingen FirebaseAdmin-NuGet (JwtBearer Authority).
- Migrering i prod: bundle som Cloud Run Job, gated af GitHub Environment + app-CLI "pending-migrations"; app nægter health ved pending.
- Største usikkerhed: email-link vs password-præ-kapring i Firebase (sign_in_provider="password" for begge?).
