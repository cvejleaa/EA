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

## Fund a0caef1 (delopgave 2, EA-register): d646a21-fundet lukket korrekt + tre nye overlevede mutationer

**d646a21-fundet ovenfor er nu rettet rigtigt** (ikke bare patched): `system-form.page.html` bruger nu
`p.type === staleVersion` (en `STALE_VERSION`-konstant importeret fra `problem.ts`) i stedet for
`p.status === 409`, og der er tilføjet en DEDIKERET test ("en navnedublet (409) tilbyder heller ikke at
kassere ændringerne") ud over 400-testen. Mutationstestet ved at sætte betingelsen tilbage til
`p.status === 409 && isEdit()` → netop den nye test blev rød. Bekræftet rettet.

**Overordnet meget stærk suite** i denne delopgave (112 API-tests + 29 web-tests). De fleste kernegrene i
`IntegrationRules.cs` er unit-testet med præcis streng-lighed pr. gren (samme mønster som
`SystemRulesTests.DeleteBlockedReason`: hver del + kombinationen testet separat). Dræbte alle mutationer
jeg prøvede der: `viaChanged=false`-guard, "ende vinder over via" i `RelationTo`, `AreNullsDistinct(false)`
i dublet-indekset (**pas på**: skabelonen migreres fra migrations-FILEN, ikke den live model — muter selve
migrationsfilen `Data/Migrations/<dato>_Integrationer.cs`, ikke kun annotationen i `EaDbContext.cs`, for at
ramme testdatabasen reelt), `canAdd`, `LocalModule`/`Counterpart`-swap, `FamilyOf` (moduler fjernet fra
familien), docs-kontrakttesten (fjern `DirekteDb`-backticks → rød).

**Overlevede — reelle huller:**
1. **`IntegrationEndpoints.Summarize`: DirectDb-tælleren ekskluderer Via** (`i.Relation !=
   IntegrationRelation.Via && i.Integration.Type == IntegrationType.DirekteDb`). Fjernede
   `!= Via`-betingelsen → HELE suiten (112/112) forblev grøn. Intet fixture har en DirekteDb-
   integration, der går VIA en platform, for det viste system (`ImpactTests`' platform Q ser kun
   Api/Event-typer). Mangler: en test i `ImpactTests.cs` med en DirekteDb-integration via en platform,
   der bekræfter den IKKE tælles med i `Summary.DirectDb` for platformens visning.
2. **Sortering: "Via hører til Ud-gruppen" er kun bevist via enum-deklarationsrækkefølge, ikke adfærd.**
   `OrderBy(i => i.Relation)` i `IntegrationEndpoints.ForSystem` er kun korrekt, fordi `IntegrationRelation`
   er deklareret `Ud, Via, Ind, Intern`. Byttede om til `Ud, Ind, Intern, Via` → KUN
   `OpenApiContractTests` blev rød (tilfældighed: enum-serialiseringsrækkefølge i kontrakten), de 111
   øvrige (inkl. alle `ImpactTests`-rækkefølgetjek) forblev grønne — fordi intet fixture har et system,
   der ser en BLANDING af Via og Ind/Intern samtidig (platformen Q ser kun Via; forælderen P ser aldrig
   Via). Mangler: en test hvor det viste system har både en Via- og en Ind/Intern-relation, der
   bekræfter Via ligger FØR Ind/Intern i den returnerede rækkefølge.
3. **`Csv.Field`: CR (`\r`) som formel-udløsende tegn er udækket.** `FormulaStart` har 6 tegn
   (`= + - @ TAB CR`), men `Formler_neutraliseres_med_apostrof`-theorien i `CsvTests.cs` har kun 5
   `InlineData`-cases (mangler `\r`). Fjernede `'\r'` fra arrayet → hele suiten forblev grøn. Mangler:
   `[InlineData("\rCR")]` i den theory.

**Svaghed i selve bevisførelsen (ikke rapporteret som "overlevet mutation", men værd at kende næste
gang):** Testen `En_foraeldet_version_afvises_ogsaa_naar_kun_dataobjekterne_aendres`
(`IntegrationEndpointsTests`) skal bevise, at `UpdatedAt`-touchet i `UpdateIntegration` tvinger et
samtidighedstjek, selv når kun dataobjekter ændres. Fjernede linjen `integration.UpdatedAt =
time.GetUtcNow();` → testen forblev GRØN (kun en anden test, der læser selve UpdatedAt-værdien, blev
rød). Årsag: testens `seen.ToUpdate()` genbruger den GAMLE DTO til det andet PUT, så `Description`
utilsigtet revideres tilbage til `null` — DEN ændring alene (ikke UpdatedAt-touchet) får EF til at
markere integrations-rækken Modified og dermed køre samtidighedstjekket. Bekræftet med en isoleret
ekstra test (samme scalar-værdier i begge PUT-kald, kun dataobjekter forskellige, forældet version):
den fejlede SELV MED den umuterede kode, fordi `FakeTimeProvider` er frosset (`app.Time.Advance()` ikke
kaldt) — `time.GetUtcNow()` returnerer samme værdi begge gange, EF ser derfor ingen ændring på
`UpdatedAt`, og integrations-rækken udelades helt fra UPDATE'en. **Mønster at genkende næste gang**: en
test, der genbruger den oprindelige DTO til et opfølgende PUT for at fremtvinge en forældet version, kan
utilsigtet også ændre ET ANDET felt tilbage — testen beviser så noget andet end den påstår. Spørg: "hvis
jeg fjerner PRÆCIS den linje, testen skal bevise, bliver den så rød?". Sandsynligvis lav produktionsrisiko
(en rigtig systemklokke ændrer sig næsten altid mellem to HTTP-kald), men testen i sig selv beviser det
ikke for netop "kun dataobjekter ændres"-scenariet.

## Generel lektie
`if (false)`/direkte konstant-udkommentering af en gren udløser ofte C# CS0162
("Unreachable code") som fejl (TreatWarningsAsErrors=true i dette repo) og stopper builden
FØR testene kan afsløre noget. Brug i stedet en betingelse compileren ikke kan bevise er
konstant (fx `Environment.TickCount == -1`, eller fjern selve linjen/branchen helt i stedet
for at "slukke" den med en litteral).
