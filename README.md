# EA-register

Et letvægts enterprise-arkitektur-værktøj ("LeanIX-light") for
IT Forretningsløsninger på DTU: et register over systemer, hvem der ejer dem
(også i forretningen), hvad der hænger sammen — og senere teknologi,
sårbarheder, SOP'er og kontrakter. Plan, faser og beslutninger står i
[`docs/plan.md`](docs/plan.md).

> **Prototype med fiktive data.** Rigtige DTU-data må aldrig committes her.

## Status

Delopgave 1 — systemregistret:

- Systemer med aliaser, type, livscyklus, forvaltende team og moduler (ét
  niveau, fx en ERP-suite med moduler).
- Roller pr. system: **forretningsejer** (den i forretningen, der ejer
  processen), systemejer og systemforvaltere. Personer har en afdeling, så
  listen viser forretningsvinklen.
- Søgning i navn, aliaser, beskrivelse og forælder. Filtre på status, type,
  team og "uden forretningsejer/team", så EA kan finde hullerne.
- Friskhed: "senest bekræftet" pr. system og knappen **Bekræft uændret**.
- Adgang håndhæves på serveren: alle skal være logget ind. I prototypen er det
  kun enterprise arkitekten (rollen `EA.Admin`), der redigerer.

## Kør lokalt

Kræver .NET SDK 10, Node 24 og PostgreSQL 16.

```bash
./scripts/dev-db.sh                                   # start PostgreSQL + opret rolle/database
dotnet run --project src/Ea.Api --launch-profile http # API på :5080 — migrerer og seeder fiktive data
cd web && npm ci && npm start                         # web på :4200 (proxy til API'et)
```

Åbn http://localhost:4200 og vælg en fiktiv bruger. *Eva Arkitekt* kan
redigere, *Leo Læser* kan kun læse.

Test-, lint- og build-kommandoerne står i [`CLAUDE.md`](CLAUDE.md#test-kommandoer).

## Struktur

| Sti | Indhold |
|---|---|
| `src/Ea.Api/` | ASP.NET Core-API (minimal APIs, EF Core + PostgreSQL), feature-mapper |
| `src/Ea.Api/Authorization/` | Det ene sted, adgang afgøres |
| `tests/Ea.Api.Tests/` | xUnit v3: regler, HTTP-tests mod rigtig PostgreSQL, kontrakttest |
| `web/` | Angular 22 + Angular Material, Vitest |
| `web/src/api/` | API-kontrakten (`openapi.json`) og de genererede TypeScript-typer |
| `.claude/` | Gennemgangsrollerne og deres hukommelse (se `CLAUDE.md`) |

## Arbejdsgang

Hver ændring gennemgås af faste roller (Test Manager, Quality Control,
Release Manager) og efter behov Arkitekt, Security Reviewer og
domæne-rådgiver — se [`CLAUDE.md`](CLAUDE.md). Projektet er oprettet fra
skabelonen `cvejleaa/skabelon`, og alle tilpasningsfelter er udfyldt.
