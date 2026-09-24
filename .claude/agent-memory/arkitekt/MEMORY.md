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
