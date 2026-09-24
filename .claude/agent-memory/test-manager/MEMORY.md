# Test Manager — mutationsfund og faldgruber

## Projekt
EA-register "LeanIX-light" for ITFL/DTU. API: src/Ea.Api (ASP.NET Core 10 + PostgreSQL,
Microsoft.Testing.Platform xUnit v3). Web: web/ (Angular 22, Vitest).

## Miljø-fælde: delt testskabelon ved parallelle kørsler
`tests/Ea.Api.Tests/Infrastructure/TestDatabase.cs` DROP/CREATE'r en FAST navngivet
skabelon-database `ea_test_template` én gang pr. proces (statisk `_templateReady`-flag).
Kører to `dotnet test`-processer mod samme PostgreSQL-server samtidig (fx hovedcheckout +
Test Manager-worktree), race'r de om skabelonen: den ene kørsel kan fejle med
"template database ... does not exist" eller 500 midt i migrering — en fejl der IKKE
skyldes en mutation. **Genkendes på**: fejlbeskeder der nævner `ea_test_template` eller
migrering, ikke en konkret assertion (`Assert.*`). Mistanke om det → kør testen isoleret
igen og se på den FULDE fejltekst (grep efter `^failed|Assert\.`), ikke kun antal fejl.
Et "dræbt" mutation-resultat, hvor antallet af fejlede tests er højere end ved en isoleret
gentagelse, er IKKE i sig selv et problem (kan skyldes den samme kontaminering) — men
kilden skal bekræftes med den konkrete assertion-tekst, ikke kun tallet.

## Fund d646a21 (delopgave 1, EA-register): ét overlevet mutation
`web/src/app/systems/system-form.page.html`: knappen "Hent nyeste version (dine ændringer
kasseres)" vises ved `@if (p.status === 409 && isEdit())`. Mutation: fjern `p.status === 409
&&` (kun `@if (isEdit())` tilbage) → **suiten forblev grøn** (27/27 i `npm test`). Årsag:
testen "viser serverens feltfejl ved roller" (`system-form.page.spec.ts`) sender en 400 og
tjekker kun at fejlteksten for roller vises — ikke at reload-knappen er FRAVÆRENDE ved
ikke-409-fejl. Konsekvens hvis nogen laver denne fejl i virkeligheden: en almindelig
valideringsfejl (fx "Et system kan kun have én forretningsejer") ville også vise en knap,
der lover at hente nyeste version og kassere brugerens indtastning — vildledende og kan
koste data unødigt. **Mønster**: "vagt der genkendes på fravær" — matcher CLAUDE.md's
punkt, men her er det stavnavnet der er præcist (409-specifik betingelse), ikke bare
tilstedeværelse/fravær generelt. Test der mangler: assertion i "viser serverens feltfejl
ved roller" (eller ny test) om at `[data-testid="problem"] button` (reload-knappen) IKKE
findes ved en 400-fejl.

## Bekræftet solidt i d646a21 (mutationer kørt og dræbt, se rapport for fuld liste)
- Adgang: EditSystemHandler (rolle-tjek), CreateSystem/ManagePersons-policies, FallbackPolicy,
  Dev-mode startup-vagt, rogue AllowAnonymous på et beskyttet endpoint.
- SystemRules: alle 4 ValidateParent-grene enkeltvis, ParentChangeBlockedReason,
  DeleteBlockedReason, ValidateRoles (dublet-gren OG begge ental/flertal single-holder-grene
  hver for sig: forretningsejer/systemejer), NormalizeAliases.
- SystemEndpoints: samtidighedstjek i PUT og confirm hver for sig, "Bekræft uændret" der
  fejlagtigt rører data (Touch i stedet for kun bekræftelse), Touch() i CreateSystem, unik-
  navn-konfliktbeskeden (top-niveau vs. under forælder), teamId=none og businessOwnerId=none
  filtre hver for sig, søgning på forælders navn/alias, %-escaping i søgning, matchedAlias,
  total (ufiltreret vs. filtreret), rækkefølge (moduler under forælder), rolle-diff (fjernet
  RemoveAll → gamle roller bliver hængende).
- Web: permissions-styret "Rediger"-knap, deaktiveret slet-knap + begrundelse, status uden
  standardværdi, loading-tilstand (formular vises ikke tomt under hentning), relativeAge
  begge grænser (31 dage, 365 dage — testet med GAMMEL og NY værdi i samme assertion-sæt),
  ental/flertal "modul/moduler", interceptor URL-scoping (kun /api/) og 401-logout/navigate.
- OpenApiContractTests: bekræftet rød ved DTO-ændring (tilføjet felt til SystemListItem).

## Generel lektie
`if (false)`/direkte konstant-udkommentering af en gren udløser ofte C# CS0162
("Unreachable code") som fejl (TreatWarningsAsErrors=true i dette repo) og stopper builden
FØR testene kan afsløre noget. Brug i stedet en betingelse compileren ikke kan bevise er
konstant (fx `Environment.TickCount == -1`, eller fjern selve linjen/branchen helt i stedet
for at "slukke" den med en litteral).
