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

## Fund 36c0817 (delopgave 2, WEB, EA-register): 9 kernepåstande dræbt, 3 nye overlevede mutationer

Alle 9 udpegede kernepåstande (retning fra/til, fritekst-guard mod "det andet system", platform-
filter + selv-eksklusion i kandidater, "Hent nyeste version" kun ved stale-version, PUT sender
`version` + enderne skrivebeskyttede ved redigering, tællelinjens ental/flertal + betingede led
(lokale løsninger/via platform) + directDb-advarsel, canAdd/canEdit-styrede knapper,
`fileNameFrom`'s `filename*`-præference + revoke, `systemDisplayName`) blev bekræftet DØDE ved
mutation — presist, streng lighed pr. gren (mønster fra a0caef1 fortsat). `npx ng test --watch=false`
fra `web/` med `PATH=/opt/node24/bin:$PATH` er kørende testkommando (53/53 baseline, 8 filer).

**Overlevede — reelle huller, alle i `integration-form.page.ts`/`system-list.page.ts`:**
1. **`canManageDataObjects()`-gating er slet ikke testet.** Byttede `computed(() => this.auth.me()
   ?.permissions.canManageDataObjects ?? false)` til `computed(() => true)` → suiten (53/53) forblev
   grøn. Alle tests i `integration-form.page.spec.ts` kører med `me(true)` (admin) — der er intet
   scenarie med en ikke-priviligeret bruger, der bekræfter, at "Dataobjektet findes ikke på listen?"-
   knappen er SKJULT. Brud på samme mønster, som ellers er godt dækket for Tilføj/Rediger-knapperne
   (`canAdd`/`canEdit`) i `system-integrations.component.spec.ts`. Mangler: en test der sætter
   `me(false)` og bekræfter knappen er fraværende.
2. **Hele `addDataObject()`-metoden er dødt kode ift. dækning.** Gjorde metoden til en tidlig
   `return;` (POST til `/api/data-objects`, sortering af listen, tilføjelse af det nye id til
   `dataObjectIds`, nulstilling af feltet — alt sammen) → suiten forblev grøn, 0 fejl. Ingen test
   rører `addDataObject`, `newDataObject` eller `createDataObject`. Mangler: en test der klikker
   "Tilføj", udfylder navnet, sender POST, og bekræfter (a) det nye dataobjekt lander i `dataObjectIds`
   ved submit, og (b) listen vises sorteret.
3. **`downloadSystemList()` (system-list.page.ts, "Hent systemliste (CSV)"-knappen) er utestet.**
   Gjorde metoden til en no-op (fjernede kaldet til `integrationsApi.downloadSystemList()` og
   fejlhåndteringen) → suiten forblev grøn. `system-list.page.spec.ts` blev ikke udvidet i denne PR,
   selvom knappen og metoden er nye. Mangler: en test der klikker `[data-testid="download-systems"]`
   og bekræfter GET mod `/api/systems/export.csv`.

**Bekræftet ekstra solidt, MEN med en overlevet mutation udover de 9 punkter:** `flags()`-metoden i
`system-integrations.component.ts` har to UAFHÆNGIGE betingelser (livscyklus ≠ IDrift, type =
LokalLoesning) — mutationstestet ved at duplikere anden betingelse ind i den første
(`if (system.type === 'LokalLoesning')` i stedet for `if (system.lifecycleStatus !== 'IDrift')`):
suiten forblev grøn (53/53), fordi test-fixturet 'Lønudtræk' altid har BEGGE egenskaber sammen
(`type: 'LokalLoesning', lifecycleStatus: 'Udfases'`) — et klassisk "to grene, ét fixture"-hul.
Mangler: et system i fixturet med KUN den ene egenskab (fx `lifecycleStatus: 'Udfases', type:
'Egenudviklet'`) for at bevise de to flag-kilder er uafhængige.

**Mønster at genkende næste gang**: "canX()"-gating, der styrer en synlig knap/sektion, testes ofte
KUN i den positive retning (admin ser knappen), fordi testopsætningens `beforeEach` sætter en fast
admin-bruger (`me(true)`) — se også a0caef1's canAdd/canEdit-fund, der VAR dækket, fordi
`system-integrations.component.spec.ts` eksplicit skifter `TestBed`-modul og re-renderer med
`canAdd: false`. `integration-form.page.spec.ts` gør ikke det samme for `canManageDataObjects`.
Tjek altid: findes der et scenarie i specs, hvor den relevante permission-flag er FALSE, ikke kun
TRUE?

## Fund 840d4c4 (delopgave 3a, server, EA-register): kapabilitetskort + CSV-import — meget stærk kerne, tre kategorier af huller

**Kernen (Csv.Unguard/Read, CapabilityImport.Parse/Plan/Apply, endpoint Import, CapabilityRules.CodeOrder/Ordered,
golden-fil+docs) er exceptionelt grundigt mutationstestet.** Kørte ca. 25 målrettede mutationer i
`Common/Csv.cs`, `Common/CsvRead.cs`, `Capabilities/CapabilityImport.cs`, `Capabilities/CapabilityRules.cs`,
`Capabilities/CapabilityEndpoints.cs`, `Capabilities/CapabilityCsv.cs`, `Auth/AuthSetup.cs` og
`docs/csv-kapabiliteter.md` — ALLE dræbt undtagen dem nævnt nedenfor. Baseline 65/65 i namespacet
`Ea.Api.Tests.Capabilities`. Bemærkelsesværdigt: ringdetektionens `chain.Contains(next)`-vagt, fjernet, gav
ikke en assertion-fejl men en UENDELIG LØKKE (måtte `timeout 60`+`pkill -9` for at redde miljøet) — dvs. koden
har ingen løkke-beskyttelse (fx max-iterationer), kun selve logikken forhindrer uendelig kørsel. Dræbt (i den
forstand at fjernelsen tydeligt IKKE er sikker), men risikabelt at gentage uden timeout-wrapper.

**Nyt mønster: forward-reference i `Apply` er reelt dækket, men ikke ved det oplagte scenarie.**
`CapabilityImport.Apply` forudberegner ALLE ids (nye og eksisterende) i et loop FØR hovedloopet, netop for at
understøtte at en række kan referere en `ForælderKode`, der optræder SENERE i filen (rækkefølgen er erklæret
ligegyldig i `docs/csv-kapabiliteter.md`). Fjernede forudberegningen og flyttede id-oprettelsen ind i
hovedloopet (lazy) → `KeyNotFoundException` og netop testen
`Soeskende_ordnes_efter_kode_med_tal_som_tal_uanset_filens_raekkefoelge` (K1.10 med ForælderKode "K1" nævnt
FØR selve K1-rækken i filen) blev rød. Denne test er ikke navngivet efter forward-reference, men beviser det
alligevel — værd at genkende: en søskende-sorterings-test kan tilfældigt også dække en helt anden mekanisme.

**Overlevede — reelle huller, alle i validerings-/robusthedslaget, INGEN i selve import/plan/apply-kernen:**
1. **Feltlængde-grænserne er helt utestede.** `CapabilityRules.CodeMaxLength` (40), `NameMaxLength` (200) og
   `DescriptionMaxLength` (4000) håndhæves i `CapabilityImport.Parse`, men INGEN test i `BadFiles()`
   (`CapabilityImportTests.cs`) rammer dem. Fjernede alle tre tjek (`code.Length > CodeMaxLength`,
   `name.Length > NameMaxLength`, `description?.Length > DescriptionMaxLength`) hver for sig → suiten (65/65)
   forblev grøn hver gang. Kun docs-testen `Vejledningen_naevner_hver_kolonne_og_graenserne` nævner tallene i
   teksten — ingen funktionel test sender en for lang kode/navn/beskrivelse. Mangler: tre `[InlineData]`-cases
   i `BadFiles()`, en pr. grænse (fx kode på 41 tegn, navn på 201 tegn, beskrivelse på 4001 tegn).
2. **`CapabilityRules.MaxRows` (5000 rækker) er utestet.** Samme mønster: fjernede
   `csv.Rows.Count > MaxRows`-tjekket i `Parse` → suiten forblev grøn. Kun docs-testen nævner grænsen i tekst.
   `IntegrationCsv`/`SystemCsv` (delopgave 1-2) har muligvis samme mønster — værd at tjekke ved lejlighed.
   Mangler: en test der bygger 5001 rækker og forventer fejlen "højst 5000 kan indlæses ad gangen" (ligesom
   `En_for_stor_fil_afvises` gør for byte-grænsen, som ER dækket).
3. **`LOCK TABLE`-sætningen i `Import`-endpointet (samtidighedslåsen) er utestet.** Fjernede linjen
   `await db.Database.ExecuteSqlRawAsync("LOCK TABLE ea.capabilities IN SHARE ROW EXCLUSIVE MODE", ct)` →
   suiten (65/65) forblev grøn. `Capability`-entiteten har INGEN `Version`/row-version-kolonne (modsat
   `SystemEntity.Version`), så LOCK'en er den ENESTE beskyttelse mod en ægte race: to samtidige commits, der
   begge læser samme `existing`-tilstand, begge beregner et matchende fingeraftryk og begge skriver — uden
   LOCK kan den ene overskrive/tabe den andens ændringer under PostgreSQLs standard READ COMMITTED-isolation.
   Ingen test i repoet bruger `Task.WhenAll`/parallelle HTTP-kald til at teste ægte databaseniveau-race
   (samtidigheds-testene, inkl. `Er_kortet_aendret_siden_toer_koerslen_afvises_importen` her, er SEKVENTIELLE
   —det ene kald venter på det andet, hvilket kun beviser fingeraftryks-sammenligningen, ikke selve låsen).
   Mangler: enten en egentlig parallel-race-test (to `Task`'er der begge poster med samme forældede
   fingeraftryk-udgangspunkt, afstemt med en kort forsinkelse i request-behandlingen) eller en accepteret
   note om, at LOCK'en er bevidst udokumenteret-ved-test defense-in-depth. Det er reelt en huller-kategori,
   CLAUDE.md selv fremhæver ("samtidighed") — bør nævnes til Quality Control/Release Manager, selv hvis Test
   Manager ikke blokerer alene på den (svær at teste deterministisk uden at indføre kunstig forsinkelse i
   produktionskoden).

**Ikke undersøgt (uden for opgavens mutationsliste, men værd at nævne):** `DevSeed.SeedCapabilitiesAsync` er
utestet (som al anden DevSeed-kode i repoet — konsistent, ikke et nyt hul), og web-siden af kontrakten
(`web/src/app/core/problem.ts`s `STALE_DRY_RUN`) har endnu ingen bruger, da 3a-web ikke er bygget endnu.

## Generel lektie
`if (false)`/direkte konstant-udkommentering af en gren udløser ofte C# CS0162
("Unreachable code") som fejl (TreatWarningsAsErrors=true i dette repo) og stopper builden
FØR testene kan afsløre noget. Brug i stedet en betingelse compileren ikke kan bevise er
konstant (fx `Environment.TickCount == -1`, eller fjern selve linjen/branchen helt i stedet
for at "slukke" den med en litteral).
