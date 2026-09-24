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
