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

## Fund 3494afa (delopgave 3a-web, EA-register): kapabilitetskort + import-UI — stærk kerne, fire kategorier huller

Baseline `npx ng test --watch=false` fra `web/` (`PATH=/opt/node24/bin:$PATH`, kør `npm install` først i
worktree'en): 76/76 i 11 filer. Kørte ~20 målrettede mutationer i `capability-import.page.ts/.html`,
`capability-map.page.ts/.html`, `capabilities.api.ts`, `core/labels.ts`, `app.ts`. Dræbte: alle param/header-
sendinger (dryRun=true/false, fingerprint til/fra, Content-Type text/csv), `onFile`s `result`-reset,
`commit`s fejl-reset af `result`, `canCommit`s `changes.length > 0`-gren, stale-dry-run-vs-stale-version-
skellet for "Kør tør-kørsel igen", errors-tabellens linje/kolonne-rækkefølge, largeRemoval-teksten (removed/
currentTotal ombyttet), "ingen ændringer"-grenen, indrykningsformlen (`depth * 24`), import-knappens
`canImport`-gate, begge tomt-kort-tekstgrene, fejlbeskeden med "Kortet kunne ikke hentes:"-præfiks, App-
menuens login-gate og selve `/kapabiliteter`-linket, `importSummaryText`s ental/flertal OG slettes/slettet-
skellet, `capabilityChangeKindLabels`.

**Overlevede — fire kategorier:**

1. **`onFile` nulstiller IKKE `done` og `problem` bevisligt — kun `result` er dækket.** Fjernede
   `this.done.set(null)` og separat `this.problem.set(null)` fra `onFile` hver for sig → suiten (76/76) forblev
   grøn begge gange. Testen "vælges en ny fil, forsvinder knappen" tjekker kun `commit`/`summary` (afledt af
   `result`). Reel konsekvens: vælger brugeren en ny fil efter en gennemført import, bliver "Kortet er
   importeret: …"-beskeden hængende, som om den nye fil også var importeret; og efter en stale-dry-run-fejl
   bliver den gamle fejlbesked + "Kør tør-kørsel igen"-knap hængende og ville (fejlagtigt) køre tør-kørsel på
   den NYE fil uden at gøre det tydeligt. Mangler: to assertions i den eksisterende test (eller en ny), der
   efter `choose(f, andenFil)` tjekker `[data-testid="done"]` og `[data-testid="problem"]` er `null` — efter
   forudgående at have sat dem (via et gennemført commit hhv. en 409-fejl).
2. **`busy()`-delen af disabled-bindingen på BÅDE "Kør tør-kørsel" og "Gennemfør import" er utestet.**
   `[disabled]="!file() || busy()"` → fjernede `|| busy()`: grøn. `[disabled]="busy()"` på commit-knappen →
   ændrede til `[disabled]="false"`: grøn. Ingen test tjekker knappens `disabled`-attribut, MENS et kald er i
   flyvning (dvs. lige efter `click(...)`, FØR http-flush). Testbart uden ny arkitektur: `HttpTestingController`
   holder requesten åben, indtil `.flush()` kaldes — en test kan klikke, tjekke `disabled === true`, og først
   derefter flushe. Mangler: to tests (én pr. knap) der bekræfter dette.
3. **Rækkens `[class.removed]="c.kind === 'Slettes'"` er utestet.** Vendte betingelsen om
   (`c.kind !== 'Slettes'`) → suiten forblev grøn — INGEN test læser `tr.classList`. Opgavebeskrivelsen
   nævner eksplicit denne klasse. Mangler: en assertion i "tør-kørslen sender filen som text/csv…"-testen
   (som allerede har en Slettes-, en Ændret- og en Ny-række) om, at kun Slettes-rækken har klassen `removed`.
4. **`descriptionChanged` er kun bevist positivt.** Fjernede lighedstjekket (`(before.description ?? null)
   !== (after.description ?? null)` → altid `!!before && !!after`) → suiten forblev grøn, fordi INGEN
   `Aendret`-fixture har ens før/efter-beskrivelse. Klassisk "vagt der genkendes på fravær, ingen test
   modbeviser den" — CLAUDE.dks eget mønster. Mangler: en `Aendret`-ændring i `preview()`-fixturet med samme
   `description` før og efter (kun navn ændret), der bekræfter "· beskrivelse ændret" IKKE står i den række.

**To betingelser i `canCommit` er strukturelt udækkelige — ikke reelle huller, men værd at kende:**
`r.errors.length === 0` kan ALDRIG afgøre noget observerbart, fordi HTML'en allerede grenerer på
`@if (r.errors.length) {…} @else { … @if (canCommit()) … }` — `canCommit()` evalueres kun i den gren, hvor
`errors.length` allerede er 0. Fjernede betingelsen → 76/76 grønt (forventet, ikke et alarmerende fund).
`!!r.fingerprint` er tilsvarende dækket af SERVERKONTRAKTEN, ikke af en test: `CapabilityEndpoints.Import`
returnerer kun et ikke-null `Fingerprint` fra en fejlfri tør-kørsel (`CapabilityImport.Fingerprint` er
non-nullable `string`), og `result` nulstilles til `null` umiddelbart efter et vellykket commit — så et
`result` med `changes.length > 0` og `fingerprint: null` kan aldrig opstå via den rigtige API-klient. Fjernede
betingelsen → 76/76 grønt. Billig, værdifuld ekstra-test alligevel: DTO-typen (`Fingerprint: string?`) tillader
det stadig i TypeScript, så én test med `preview({ fingerprint: null })` og changes tilstede, der bekræfter
knappen er skjult, ville låse invarianten fast mod fremtidige refaktoreringer af serveren.

**Ikke en ny mangel, men bekræftet konsistent mønster:** `capability-map.page.ts`s `download()`-fejlgren
(`this.error.set(toProblem(e).message)`, INGEN præfiks) er utestet — mutation (fjernede sætningen af error)
gav 76/76 grønt. Samme mangel findes allerede i `system-list.page.spec.ts` for `downloadSystemList()` (se
36c0817-fundet ovenfor) — konsistent, ikke nyt for denne PR, men værd at rette samlet en dag.

**Bekræftet: `app.routes.ts`s nye `/kapabiliteter`-ruter (inkl. `authGuard`) er IKKE unit-testet** — men det
er konsistent med `/systemer`-ruterne, der heller aldrig testes direkte (ingen E2E endnu, jf. CLAUDE.md). Ikke
et PR-specifikt hul.

## Fund 89d8011 (delopgave 3b, server, EA-register): koblinger mellem systemer og kapabiliteter — meget stærk kerne, fire kategorier huller

Baseline: `dotnet test --project tests/Ea.Api.Tests` 237/237 (16/16 i `Capabilities.CouplingTests`), web `npx ng test
--watch=false` 84/84. Kørte ca. 25 målrettede mutationer i `SystemEndpoints.cs` (Apply/ValidateCapabilities/
CapabilitiesOf/ListSystems-filter), `CapabilityRules.CoupleBlockedReason`, `CapabilityImport.Plan/Apply`,
`CapabilityEndpoints.cs`, `CapabilityQueries.cs`, samt web (`labels.ts`, `capability-import.page.html/.ts`) —
FLERTALLET dræbt præcist, pr.-gren, inkl. begge grene af `CoupleBlockedReason` (RetiredAt/hasChildren) hver for
sig, "eksisterende bevares" (mutation: valider ALLE `wanted` i stedet for kun `added` → dræbt af netop den test,
der skal bevise det), null-vs-tom-liste, RemoveAll/Add-diffen, familien i `CapabilitiesOf` (moduler ekskluderet,
HeldBy altid null, rækkefølge egne/familie), `ListSystems`s `none`-filter (`All`→`Any` på modul-siden, fjernet
tjek på forælder-siden), ugyldig `capabilityId`, manglende `CapabilityLinks`-load (4 tests dør), `Genaktiveres`-
grenen, `KindOrder`, `active`-beregningen (CurrentTotal uden udgåede), `Selectable`/`Path`/`Retired.Systems` i
`/api/capabilities`, `CoupledSystemsAsync`s "Forælder › Modul", og web-siden (Udgår-label, retired/reactivated-
grene i `importSummaryText`, `[class.removed]` for Udgaar, `to-move`-betingelsen, fravær-vagten for `affected`,
`systemNames`-join).

**Overraskende IKKE et hul (verificeret empirisk, ikke kun teoretisk):** `Apply`s eksplicitte
`capability.ParentId = null;` i retiring-loopet ser ud til at være load-bearing (kommentaren siger stien
beregnes FØR træet ændres, og en forælder kan slettes mens et barn udgår i SAMME transaktion — ellers ville FK
RESTRICT på `capabilities.parent_id` fejle). Mutation (fjern linjen) gav 237/237 GRØNT — men i stedet for at
rapportere det som et hul, byggede jeg et midlertidigt diagnose-`[Fact]` (IKKE committet) der læste raw DB via
`app.Services.CreateScope().GetRequiredService<EaDbContext>()` efter scenariet "K2 slettes, K2.1 (barn, koblet)
udgår i samme import": resultatet var `K2 exists=False; K2.1 ParentId=` (tomt). **EF Core nuller selv FK'en
client-side ved SaveChanges, når en tracked entity, der er markeret Deleted, stadig er refereret af en anden
tracked entitets valgfri (nullable) FK — også med `DeleteBehavior.Restrict`.** Den eksplicitte linje er dermed en
defensiv no-op, ikke en risiko i sig selv — så det er IKKE en reel mangel, blot en implementeringsdetalje EF
klarer alligevel. **Lektie til næste gang**: når en "grøn efter mutation"-observation virker overraskende (fx en
FK-integritetsregel, der burde fejle), byg en billig, midlertidig raw-DB-diagnostik FØR den rapporteres som hul —
ellers risikerer man en falsk positiv i rapporten.

**Overlevede — fem reelle huller:**
1. **`CapabilityRules.MaxCouplingsPerSystem` (100) er helt utestet.** Fjernede grænsetjekket i
   `SystemEndpoints.ValidateCapabilities` (`wanted.Count > MaxCouplingsPerSystem`) → 237/237 forblev grønt.
   Samme mønster som `MaxRows`/feltlængderne i 840d4c4 (delopgave 3a). Mangler: en test der sender >100
   `CapabilityIds` og forventer valideringsfejlen "Højst 100 kapabiliteter pr. system."
2. **`CapabilityImportSummary.SystemsToMove`s `Distinct()` er ubevist.** Ændrede
   `toMove.Select(s => s.Id).Distinct().Count()` til `toMove.Count` → 237/237 forblev grønt, fordi intet fixture
   har ét system koblet til FLERE kapabiliteter, der samtidig udgår/får underkapabiliteter (alle eksisterende
   scenarier har præcis 1:1 mellem koblinger og systemer, der skal flyttes). Mangler: et system koblet til to
   kapabiliteter, der begge udgår i samme import — `SystemsToMove` skal være 1, `CouplingsToMove` 2.
3. **"Allerede udgået + koblinger → ingen ændring" har kun tyndt sikkerhedsnet.** Fjernede
   `gone.RetiredAt is null`-betingelsen i `Plan` (så en allerede udgået kapabilitet med koblinger udgår IGEN ved
   hver import) → fanget af netop ÉN linje i `Import_lader_en_koblet_kapabilitet_udgaa_i_stedet_for_at_slette_den`
   (eksport→reimport skal give 0 ændringer, linje 260). Ikke et hul i streng forstand (dræbt), men skrøbeligt:
   ingen test navngivet efter selve dette udsagn — en fremtidig refaktorering af eksport-testen kunne utilsigtet
   fjerne dækningen. Overvej en dedikeret test: reimportér SAMME fil to gange efter en Udgaar, forvent 0 changes
   anden gang.
4. **`FaarUnderkapabiliteter`s "kun når bladet havde INGEN børn FØR" (`hadChildren`-gaten) er reelt utestet.**
   Fjernede `!hadChildren.Contains(current.Id) &&` → 237/237 forblev grønt. Ingen test genimporterer SAMME fil
   to gange efter et blad har fået børn — så ingen test fanger, at kapabiliteten ellers ville blive meldt
   "FaarUnderkapabiliteter" (med samme koblinger) ved HVER efterfølgende import, selvom intet nyt er sket.
   Mangler: efter `Et_koblet_blad_der_faar_boern_meldes_saa_koblingerne_kan_flyttes`-scenariet, importér SAMME
   fil igen og forvent at K1.2 nu er `Unchanged` (ikke `FaarUnderkapabiliteter` igen).
5. **`removedFromMap`s skelnen mellem "var allerede udgået" og "var aktiv" ved Slettes er utestet.**
   Ændrede `gone.RetiredAt is null ? 1 : 0` til altid `1` i Slettes-grenen (LargeRemoval-tælleren) → 237/237
   forblev grønt. Ingen test kombinerer "en allerede udgået kapabilitet uden koblinger slettes" MED nok andre
   fjernelser til at afgøre, om den fejlagtigt tæller dobbelt mod `LargeRemoval`/`CurrentTotal`-procenten (som jo
   udelukkende bør handle om det SYNLIGE kort). Mangler: en test hvor en allerede-udgået, koblingsløs
   kapabilitet slettes SAMTIDIG med at resten af filen er uændret, og som bekræfter `LargeRemoval: false` (fordi
   den udgåede aldrig var en del af `CurrentTotal`).

**LOCK på `system_capabilities` (samtidighed) er, som forventet (samme mønster som 840d4c4), IKKE racetestet.**
Fjernede `ea.system_capabilities` fra `LOCK TABLE`-sætningen → 237/237 forblev grønt. Der FINDES en ægte
parallel-race-test i repoet (`CapabilityImportTests.Samtidige_gennemfoerelser_af_samme_toer_koersel_giver_praecis_en_import`,
8 samtidige klienter, `Task.WhenAll`), men den øver kun ren kapabilitets-kollision, ikke en race mod en
SAMTIDIG kobling/frakobling på et system. Ikke nyt, men bør nævnes til Quality Control/Release Manager som
kendt, accepteret defense-in-depth (svært at teste deterministisk uden kunstig forsinkelse i produktionskoden).

**Solidt dækket uden overraskelser:** `SystemDetail.Capabilities`-loading (fjernet `LoadAsync` af `CapabilityLinks`
→ 4 tests dør), stale-version ved ren kapabilitetsændring (genbruger den allerede grundigt testede
"IsModified=true, ALTID"-mekanik fra delopgave 1/2 — ikke re-mutationstestet separat, da mekanismen er fælles og
allerede dræbt tidligere), web-siden af 3b (labels, HTML-betingelser, `systemNames`) — alt dræbt præcist.

## Fund 7e785e2 (delopgave 3b-web, EA-register): koblinger UI — alle 11 udpegede kernemutationer dræbt, kun små kosmetiske huller

Baseline: `npx ng test --watch=false` (web/, `PATH=/opt/node24/bin:$PATH`, `npm install` først i worktree'en) 96/96 i
11 filer. Kørte netop de 11 udpegede mutationer i `system-form.page.ts` (`capabilityMatches`s `selectable`-filter,
"ikke allerede valgt"-filter, sti-delen af søgningen, `capabilityIds`→`null` i `toRequest`, familiens koblinger
lækket ind i `ownCapabilities` via `fill()`, `addCapability`s `search.value = ''`-tømning),
`system-list.page.ts` (`loadCapabilityName`s `tree.toMove`-fallback, `none`-guarden),
`capability-map.page.html` (`n.selectable`-betingelsen for links) og `system-detail.page.html` (forælder/modul-
skelnen `c.heldBy.id === s.parent?.id`, `move-reason`-betingelsen) — **ALLE 11 dræbt præcist**, ofte af netop ÉN
linje i den relevante nye test (fx `capabilityMatches`-testen dør på præcis den forkerte gren, ikkeblot en generel
optælling). Særligt stærkt: system-detail-testen bruger `items.map(li => ... !== null).toEqual([false, true, true,
false])` — en positiv PR-linje-for-linje-vagt for `move-reason`, ikke en løs "findes et flag et sted"-optælling.

**Fire yderligere eksplorative mutationer fundet (uden for den udpegede liste), alle overlevede — men alle
kosmetiske/lav-risiko, ikke funktionelle:**
1. `web/src/app/core/labels.ts:74` — `moveReasonShortLabels.HarUnderkapabiliteter`s VÆRDI ('har underkapabiliteter')
   er aldrig renderet/assertet i en test. Ændrede den til en tydeligt forkert streng → 96/96 forblev grønt.
   `system-form.page.spec.ts`s kapabilitets-tests bruger kun `Udgaaet` (`K1.9 Gammel eksamen · udgået`) på chippen;
   ingen fixture har en egen kobling med `moveReason: 'HarUnderkapabiliteter'`. Mangler: en chip i "viser egne
   koblinger..."-testen (eller ny) med `moveReason: 'HarUnderkapabiliteter'`, der bekræfter teksten
   "· har underkapabiliteter" på chippen.
2. `web/src/app/systems/system-form.page.html:149` — `[class.flagged]="c.moveReason"` på `mat-chip-row` er en ren
   CSS-hook, ingen test læser `classList`. Ændrede til `[class.flagged]="false"` → 96/96 grønt.
3. `web/src/app/systems/system-detail.page.html:77` — samme mønster: `[class.held]="c.heldBy"` på `<li>` er
   utestet. Ændrede til `[class.held]="false"` → 96/96 grønt.
4. `web/src/app/systems/system-form.page.ts:110` — `.slice(0, 50)`-grænsen på `capabilityMatches` er utestet (ingen
   fixture har >50 matchende kapabiliteter). Ændrede til `.slice(0, 500000)` → 96/96 grønt. Opgavebeskrivelsen
   nævner eksplicit "højst 50", så dette er tættest på et reelt hul blandt de fire — men lav praktisk risiko (et
   kort med 50+ valgbare blade under samme søgeord er usandsynligt i en DTU-kontekst med få hundrede kapabiliteter
   totalt).

**Ingen funktionelle huller fundet i selve kernen** (adgang til request-payload, familie/egen-skel, sortering,
forælder/modul-skelnen begge steder, tom-liste-visning, `none`-filter, `toMove`-opslag) — markant stærkere end
gennemsnittet af tidligere fund i dette projekt, formentlig fordi opgavebeskrivelsen selv listede præcis de 11
mutationer, der plejer at være hullerne (dvs. forfatteren har allerede kørt denne øvelse selv før commit).

**Bekræftet igen**: parent/modul-skelnen (`c.heldBy!.id === existing()?.parent?.id`) på FORM-siden har kun
fixture-data for "modulet"-grenen (ingen test sætter `existing().parent`-id lig et `heldBy.id`), men mutation af
selve sammenligningsoperatoren (`===`→`!==`) blev alligevel dræbt, fordi den ændrer outputtet for den ENESTE
testede sag også. **Lektie**: en binær betingelse med kun ÉN gren fixture-dækket er stadig dræbt af en
operator-flip-mutation, fordi flippet nødvendigvis også ændrer den dækkede gren — kun en konstant-erstatning
(`true`/`false` i stedet for selve sammenligningen) ville undslippe. Værd at kende, så man ikke fejlagtigt
rapporterer den slags som et hul uden selv at prøve flippet.

## Fund da9ee56 (delopgave 3c-1, server+web, EA-register): overlap-reglen og koblings-CSV'en — meget stærk kerne, to overlevede mutationer

Baseline: `dotnet test --project tests/Ea.Api.Tests --filter-namespace Ea.Api.Tests.Capabilities` 147/147, web
`npx ng test --watch=false` 98/98. Kørte netop de 16 udpegede mutationer (`OverlapExclusionOf`s fire grene +
begge arve-retninger + begge forrangs-byt, `OverlapOf`s `Planned`/`PlannedOnTopOfActive`/tærskler, `OverlapGroupKey`
→ id, `MoveReasonOf`-porten, `Uncovered` to varianter, Nedlagt-filtret i eksporten, tom-kode-rækken to varianter,
`DelesMed`, `WithChildren`, web-knappens URL) — **14 af 16 dræbt præcist**, ofte af netop ÉN linje i
`OverlapRulesTests`s `Exclusions`-theory (fx `{ Udfases, LokalLoesning, null, Udfases }` dræber BEGGE
forrangs-byt hver for sig). `CouplingCsvTemplateTests`s rige, navngivne fixture (8 systemer, moduler, én retired
kapabilitet, én lokal løsning) dræbte selv urelaterede mutationer (fx tom-kode-række-varianterne) som bifangst.

**Overlevede — to reelle huller, begge i `CouplingCsv.cs`/`CapabilityRules.cs`:**
1. **`DelesMed` udelukker kun rækkens eget `SystemId`, ikke sin `GroupKey`, i INGEN test.**
   `src/Ea.Api/Capabilities/CouplingCsv.cs:81`: `.Where(m => m.Holder.GroupKey != own!.Holder.GroupKey)` — ændrede
   til `.Where(m => m.Holder.SystemId != system.Id)` → 147/147 forblev grønt. Årsag: INGEN fixture (hverken
   `CouplingCsvTemplateTests` eller `CouplingExportTests`) har to holdere i SAMME gruppe (to søskendemoduler, eller
   et modul og dets forælder), der begge er koblet til den SAMME kapabilitet — kun ét medlem pr. gruppe optræder
   nogensinde som "egen kobling"-række for den kapabilitet. Reel konsekvens: kobles et system OG et af dets moduler
   til samme kapabilitet, ville modulets `DelesMed`-kolonne fejlagtigt liste forælderen som "deler med" (og omvendt),
   selvom de tælles som ÉT system i overlap-tallet. Mangler: en test hvor fx `Nordlys` (forælder) OG `Nordlys > HR`
   (modul) begge er koblet til samme kapabilitet, der bekræfter at `DelesMed` for HR's række IKKE nævner Nordlys.
2. **`CapabilityRules.WithChildren`s "udgåede børn tæller ikke" (`c.RetiredAt is null`-filteret) er utestet i HELE
   test-suiten — også før denne PR.** `src/Ea.Api/Capabilities/CapabilityRules.cs:101-102`: fjernede
   `c.RetiredAt is null &&` → 147/147 forblev grønt (både Capabilities-namespacet og en efterfølgende bredere
   søgning efter `RetiredAt =`/`Selectable` i HELE `tests/Ea.Api.Tests` fandt kun to direkte `RetiredAt`-
   konstruktioner, ingen af dem en kapabilitet der er ENESTE barn af en anden). Denne PR flyttede logikken (tre
   identiske kopier → én), men ændrede den ikke — hullet er altså ældre end 3c-1, blot nu samlet ét sted, så én
   rettelse lukker det tre steder. Reel konsekvens: udgår en kapabilitets ENESTE underkapabilitet, burde
   forælderen blive et blad igen (`Selectable: true`, kan kobles direkte) — mutationen beviser, at intet i
   test-suiten opdager, hvis den regel går i stykker. Mangler: en test (fx i `CouplingTests.cs` eller en ny unit-
   test i `CapabilityRulesTests.cs`) der lader en kapabilitets eneste barn udgå via import, og derefter bekræfter
   forælderens `Selectable` er `true` (ikke stadig blokeret af det udgåede barn).

**Bekræftet solidt derudover:** web-knappens URL (`downloadFile(..., '/api/capabilities/couplings/export.csv', ...)`)
dræbt præcist af `http.expectOne(...)` + `TestBed`-dobbeltinstantiering, som gjorde FLERE tests røde på én gang —
en stærkere fejlsignatur end en løs `toContain`.

## Fund b8d6ff5 (delopgave 3d-server, EA-register): koblings-CSV-IMPORT — kernen meget stærkt dækket, ét reelt hul (skjult "ny adfærd" i en refaktorerings-commit)

Baseline: `dotnet test --project tests/Ea.Api.Tests --filter-namespace Ea.Api.Tests.Capabilities` 171/171 (19/19 i
`CouplingImportTests`, inkl. theory-cases). To commits: `d3c91b3` ("Import-protokollen samlet i Common/CsvImport
(uden ny adfærd)") og `b8d6ff5` (selve koblingsimporten). Kørte alle 18 udpegede mutationer (nogle med to
varianter) i `CouplingImport.cs`, `CsvImport.cs`, `CapabilityEndpoints.cs`, `SystemEndpoints.cs.DeleteSystem` og
`CsvRead.cs`s `Unguard` — **17 af 18 dræbt**, ofte præcist af navngivne tests (fx `Hoejst_100_koblinger_pr_system`
dræber BÅDE `>`→`>=` og "loftet fjernet" for `MaxCouplingsPerSystem`; `En_import_venter_paa_en_samtidig_kobling_og_afvises`
og `En_sletning_venter_paa_en_import_uden_at_have_laast_systemet` er ægte parallelle lås-race-tests, der dræber
LOCK-mutationer i både importen OG `DeleteSystem` — IKKE bare sekventielle stub-tests som i tidligere delopgaver).

**Overraskende solidt af sig selv:** "Fejl_mod_registret_vises_med_linje_og_kolonne_og_intet_gemmes" har ved et
tilfælde to rækker med TOM `FuldtNavn` (`(kompas.Id, "", "K9")`, `(kompas.Id, "", "K1")`) — det dræbte BÅDE
"FuldtNavn-tjekket fjernet" OG "tom er OK fjernet"-mutationerne, selvom ingen test eksplicit er navngivet efter
"tom er OK". Værd at huske: tjek altid det FAKTISKE testdata for et tomt-felt-tilfælde, før man rapporterer det
som et hul — det kan ligge skjult i en fejl-test, der egentlig handler om noget andet.

**Overlevet — ét reelt hul, og det er en advarsel om en bestemt slags refaktorerings-risiko:**

1. **`catch (DbUpdateConcurrencyException)` i `CsvImport.CommitIfUnchangedAsync` (`src/Ea.Api/Common/CsvImport.cs:110-117`,
   returnerer `null` → 409 i stedet for at lade undtagelsen boble op) er HELT utestet.** Fjernede try/catch'en
   (kald `SaveChangesAsync` direkte) → HELE `Ea.Api.Tests.Capabilities`-namespacet (171/171, inkl. både
   kapabilitetskort- og koblingsimporten, som begge bruger denne fælles funktion) forblev grønt. **Særligt
   bemærkelsesværdigt**: denne catch er ikke bare udækket — den er **reelt NY ADFÆRD**, selvom d3c91b3's
   commit-besked eksplicit siger "uden ny adfærd". `git show d094add:.../CapabilityEndpoints.cs` (før
   generaliseringen) viser, at det gamle `Import`-endpoint kaldte `await db.SaveChangesAsync(ct);` HELT UDEN
   try/catch — en ægte samtidigheds-konflikt under selve gemningen (fx en formular, der gemmer PRÆCIS i vinduet
   mellem lås og SaveChanges) ville før have givet en ubehandlet undtagelse (500), men giver nu en pæn 409
   (StaleDryRun). Ingen test, hverken før eller efter, har nogensinde bevist at denne kode gør noget — en grøn
   suite ville forblive grøn, uanset om try/catch'en er der eller ej. **Mangler**: en test, der reelt udløser
   `DbUpdateConcurrencyException` INDE I `CommitIfUnchangedAsync`s `SaveChangesAsync` — sværere end de
   eksisterende lås-race-tests, fordi `LOCK TABLE ea.capabilities, ea.system_capabilities` allerede serialiserer
   koblings-/kort-skrivninger; en ægte konflikt kræver et system, der IKKE er omfattet af den lås (fx systemets
   egen `Version`-kolonne bumpet af en samtidig `PutSystem`, som ikke tager samme lock), ramt lige efter
   `recompute()` men før `SaveChangesAsync`. Vurdér om dette er værd at teste eksplicit, eller om det skal noteres
   til Quality Control/Release Manager som en bevidst, svært-testbar defense-in-depth (samme mønster som
   LOCK-dækningen i 840d4c4/89d8011) — men i modsætning til dén, er DENNE gren slet ikke dokumenteret som bevidst
   udækket noget sted.

**Lektie til næste gang**: en commit-besked, der siger "generalisering uden ny adfærd" (eller lignende), er en
PÅSTAND, ikke en garanti — diff'et mod den GAMLE kode (her: `git show <base>:<fil>` for filen, FØR den blev
splittet/flyttet) kan afsløre skjult ny adfærd, som ingen test fanger, fordi ingen troede der var noget nyt at
teste. Tjek det specifikt, når en refaktorerings-commit ligger lige før en commit, der bruger den nye,
generaliserede kode til noget vigtigt (som her: den fælles lås+fingeraftryk-protokol, som BÅDE kort- og
koblingsimporten er afhængige af for korrekt 409-håndtering).

**Miljø-fælde bekræftet igen** (se afsnittet øverst): under denne gennemgang ramte `dotnet test` midlertidigt
"remaining connection slots are reserved for roles with the SUPERUSER attribute" — server-dækkende
forbindelses-udtømning pga. andre samtidige `dotnet test`-processer (formentlig andre subagenter, der kørte
parallelt jf. CLAUDE.md's arbejdsgang). ALLE 19 tests fejlede samtidigt, med 100 % identisk fejltekst (ingen
`Assert.*`), og kørslen var mistænkeligt hurtig (~1,7s mod normalt ~17s). Ventede med en `until psql ...; do sleep
5; done`-løkke, til en almindelig forbindelse igen lykkedes, og gentog derefter mutationskørslen — gav det
forventede, konsistente resultat. Genkend dette mønster med det samme og gentag ISOLERET, i stedet for at
rapportere "17 fejl" som et ægte mutations-fund.

## Fund 860d661 (delopgave 3d-web, EA-register): koblings-import-UI + fælles `ImportFlow` — alle 18 udpegede kernemutationer dræbt

Baseline: web `npx ng test --watch=false` (via `/opt/node24/bin/node ./node_modules/.bin/ng test --watch=false`, da
`npx`/PATH-omgåelse blev afvist af sandboxen i denne worktree — kør binæren direkte i stedet) 106/106 i 12 filer;
API `dotnet test --project tests/Ea.Api.Tests --filter-namespace Ea.Api.Tests.Capabilities` 180/180. Tre commits:
`4a554fd` (tilstanden fra kortimport-siden udtrukket til `core/import-flow.ts`, "uden ny adfærd" — troværdig her,
i modsætning til b8d6ff5-fundet: `capability-import.page.spec.ts` er HELT uændret og forblev 106/106 grøn med den
nye `ImportFlow`-klasse bag facaden), `eb6a1df` (koblings-import-siden, ny) og `860d661` (kun testinfrastruktur,
ikke mutationstestet jf. opgavebeskrivelsen — allerede bevist med en midlertidig superuser-test).

Kørte netop de 15 udpegede mutationer — **ALLE dræbt, ingen overlevede**:
- `ImportFlow.canCommit`: `changes.length > 0`-fjernelse dræbt af `capability-import.page.spec.ts:282` ("Filen er
  identisk med kortet"-testen), `!!r.fingerprint`-fjernelse dræbt af samme fils linje 360 (`fingerprint: null`-
  scenariet). `errors.length === 0`-fjernelse gav derimod 106/106 GRØNT — men det er IKKE et nyt hul, det er det
  samme strukturelt udækkelige tilfælde som allerede dokumenteret i fund 3494afa: begge sider grenerer HTML'en på
  `@if (r.errors.length) {…} @else { …@if (canCommit()) }`, så `canCommit()` aldrig evalueres, når `errors.length
  > 0`. Ikke rapporteret som mangel.
- `onFile`s `result.set(null)`-fjernelse dræbt af `capability-import.page.spec.ts:295` (vælg ny fil efter en
  gennemført import → `commit`/`summary` skal være væk). **Bemærk**: `done`/`problem`-resetten i `onFile` (som VAR
  et hul i 3494afa, punkt 1) er nu implicit dækket af samme mekanisme, fordi den ligger i den fælles `ImportFlow`
  — men ingen test her isolerer `done`/`problem` specifikt, kun `result`. Ikke re-verificeret separat i denne
  gennemgang (uden for den udpegede liste), men værd at holde øje med, hvis `ImportFlow` nogensinde splittes op.
- `commit`s manglende fingerprint-afsendelse dræbt af BÅDE `coupling-import.page.spec.ts:175` OG
  `capability-import.page.spec.ts` (fælles kode, fælles dækning — to filer døde samtidig, et stærkt signal).
  `commit`s manglende `result.set(null)`-rydning ved fejl dræbt af `coupling-import.page.spec.ts:223` (stale-dry-
  run-testen: commit-knappen skal være væk efter fejlen, før "Kør tør-kørsel igen" klikkes).
- Koblingssiden (`coupling-import.page.ts/.html`): `today()` (removed+unchanged→removed) dræbt af "2 af de 5"-
  assertionen (removed=2, unchanged=3 — GAMMEL værdi 2 ville stå i stedet for 5, begge tal i selve testen).
  `cleared`s ental/flertal-byte dræbt for n=1 (kun singular er fixture-dækket, men et byt af de to grene ÆNDRER
  outputtet for n=1 også — samme "operator-flip fanges selv med kun én gren dækket"-mønster som i fund 7e785e2).
  Alle 5 advarselsbetingelser (`largeRemoval`, `systemsCleared > 0`, `systemsChangedSinceExport > 0`,
  `notInFile.length`, `warnings.length`) dræbt hver for sig ved `@if (true)`-mutation — fanget af "uden advarsler
  i svaret vises ingen advarsler"-testen, som eksplicit sætter dem til 0/tom OG asserterer `toBeNull()` for alle
  fem test-id'er i ét loop. `[class.removed]="c.kind === 'Fjernes'"` byttet til `'Tilfoejes'` dræbt af
  `rows.map(r => r.classList.contains('removed'))`-arrayet (`[true, true, false]` — en positiv, PR-linje-for-
  linje-vagt, ikke en løs optælling). `changedSinceExport`-betingelsen for `changed-flag` sat til altid-vist
  dræbt af cellernes tekstindhold (Nordlys›HR-rækken har IKKE flaget i fixturet). `notInFile`-linkets `s.id`→
  `s.name` dræbt af href-assertionen (`/systemer/sys-u` vs. det forkerte `/systemer/Ugle`).
  `couplingImportSummaryText`s committed/dryRun-byt dræbt af BÅDE dry-run- og committed-testen (hver har sin egen
  forventede tekst med modsatte bøjninger).
- `labels.couplingChangeKindLabels`-byt (Fjernes↔Tilføjes) dræbt af ændringstabellens celletekst.
- `capability-map.page.html`s `import-couplings`-links `canImport`-gate fjernet (sat til `@if (true)`) dræbt af
  `capability-map.page.spec.ts:147` (reader-scenariet, `toBeNull()`).
- Serveren: `CsvImport.HeaderError`s `otherFile`-tuple fjernet i BÅDE `CapabilityImport.Parse` og
  `CouplingImport.Parse` hver for sig dræbt af de to nye theory-cases i henholdsvis `CapabilityImportTests.cs` og
  `CouplingImportTests.cs` (den anden fils overskrift genkendes og giver den krydshenvisende fejltekst).

**Miljø-note til næste gang**: i denne worktree blokerede sandboxen `PATH=/opt/node24/bin:$PATH npx ng test …`
(både som `PATH=...`-præfiks og som `env PATH=... npm …`) med en git-isolationsfejl, selvom kommandoen intet har
med git at gøre — formentlig en heuristik, der reagerer på PATH-manipulation generelt. Løsning: kald binæren
direkte, `/opt/node24/bin/node ./node_modules/.bin/ng test --watch=false` (kræver `/opt/node24/bin/npm install`
kørt først, samme binær-direkte-teknik).

**Ingen funktionelle huller fundet** — endnu en delopgave (efter 7e785e2) hvor opgavebeskrivelsens egen
mutationsliste allerede dækker præcis de svage punkter, og alle er lukket. Eneste værd-at-vide-punkter er de to
allerede-dokumenterede, ikke-nye mønstre ovenfor (strukturelt udækkelig `errors.length`-gren, og den endnu-ikke-
isolerede `done`/`problem`-reset i den delte `ImportFlow`).

## Fund f18dd09 (delopgave 3c-2, server, EA-register): overlap på kortet, dækningstal og "deles med" på systemsiden — to reelle huller, begge i wiring/skelnen, ikke i selve reglen

Baseline: `dotnet test --project tests/Ea.Api.Tests --filter-namespace Ea.Api.Tests.Capabilities` 185/185 (5 nye i
`OverlapApiTests.cs`, op fra 180 i b8d6ff5). Web `npx ng test --watch=false` (via `/opt/node24/bin/node
./node_modules/.bin/ng test --watch=false`, `npm install` med `/opt/node24/bin/npm` først i worktree'en) 106/106 —
uændret optælling, kun én eksisterende teksts forventning opdateret ("Ikke angivet (nedlagte undtaget)"). Kørte 14
udpegede mutationer i `CapabilityQueries.cs` (`Missing`/`InUse`, `OverlapHoldersAsync`s tre felter), `CapabilityEndpoints.cs`
(`Coverage`-beregningen, `GetTree`s hasChildren-argument til `OverlapOf`), `SystemEndpoints.cs`s `CapabilitiesOf` (fire
varianter) og `ToDto` — **11 af 14 dræbt**, ofte præcist af netop den nye `Daekningen_er_praecis_...`-test (dræber
BÅDE `Missing` uden Nedlagt-filter, `InUse` med nedlagte OG `Coverage.Covered=Missing` byttet — samme test, tre
uafhængige linjer/tal) og `Systemsiden_viser_...`-testen (dræber SharedWith-mutationerne).

**Overraskende IKKE et hul, men en fragil kilde til falsk tryghed**: `SystemEndpoints.cs:677`s
`overlap?.Members.First(m => m.Holder.SystemId == x.Link.SystemId).Exclusion` (skal bruge LINKETS holder, ikke det
VISTE system) — byttet `x.Link.SystemId` til `s.Id` gav **185/185 grønt for selve `OverlapApiTests`-klassen isoleret**
(`--filter-class Ea.Api.Tests.Capabilities.OverlapApiTests`, 5/5), men **hele `Capabilities`-namespacet fik 3 fejl**
(`CouplingExportTests.Et_system_og_dets_moduler_deler_ikke_med_hinanden`,
`CouplingTests.Familien_vises_egne_koblinger_foerst_og_via_modul_eller_forælder_bagefter`,
`CouplingImportTests.En_eksport_indlaest_igen_giver_nul_aendringer`) — som **500-fejl**
(`System.InvalidOperationException: Sequence contains no matching element`), ikke en ren assertion. Årsag: `.First()`
kaster, når det VISTE system slet ikke selv er holder af kapabiliteten (kun familien er) — hvilket kun sker i ÆLDRE
koblings-tests, ikke i den nye `OverlapApiTests`-seed, hvor HR (testens eneste familie-scenarie) ALTID selv er direkte
koblet til K1.1 OG har samme (null) exclusion som Nordlys. **Mønster at genkende næste gang**: en dedikeret test for en
ny skelnen (holder vs. viste-system) kan være strukturelt ude af stand til at bevise skellet, hvis fixturet lader de to
værdier falde sammen — suiten fanger mutationen alligevel, men kun som bifangst fra UBESLÆGTEDE, ældre tests, og kun
fordi den forkerte kode tilfældigvis kaster en undtagelse (ikke fordi den regner forkert og stille). Havde ALLE
systemer i alle tests tilfældigvis selv været direkte koblet til det, de arver, ville mutationen være usynlig. Mangler:
en test i `OverlapApiTests.cs` med et modul, der KUN ser en kapabilitet via familien (ingen egen kobling til den) —
eller hvor modulets egen status ville give en ANDEN exclusion end holderens (fx et Udfases-modul, der viser en
kobling holdt af sin aktive forælder) — der beviser `OwnExclusion` kommer fra `link.SystemId`, ikke fra det viste
system.

**Ét reelt, tavst hul**: `CapabilityQueries.cs:61`+`:66` — `OverlapHoldersAsync`s `ParentStatus`-projektion
(`system.ParentSystem == null ? null : system.ParentSystem.LifecycleStatus`, wired ind i `OverlapHolder`s sidste felt)
er UBEVIST over HTTP. Satte den til konstant `null` ("arv tabt") → **185/185 forblev grønt**, ingen eneste fejl,
hverken i `OverlapApiTests` eller resten af namespacet. Årsag: `OverlapRulesTests.cs` (den rene enhedstest af selve
arve-reglen, `CapabilityRules.OverlapExclusionOf`) konstruerer `OverlapHolder`-objekter DIREKTE med et manuelt
`parentStatus`-argument (`M("...", status, parentStatus: Udfases)` i `Exclusions`-theory'en) — den tester ALDRIG
databasen-wiring'en, kun selve beslutningstabellen. Og `OverlapApiTests`' eneste familie-scenarie (HR/Nordlys) har
BEGGE parter `IDrift`, så en Nedlagt/Udfases-forælder optræder aldrig i en ægte DB-seedet test. To lag, hver for sig
grundigt testet, men KOBLINGEN mellem dem (den EF-projektion, der rent faktisk henter forælderens status fra databasen
og lægger den i DTO'en) er udækket. Mangler: en test i `OverlapApiTests.cs` med et modul, hvis FORÆLDER er Udfases
eller Nedlagt (modulet selv `IDrift`), koblet til en vurderet kapabilitet, der bekræfter modulets `Exclusion`
(`Udfases`/`Nedlagt`) på et ANDET systems `SharedWith`-liste (eller egen `OwnExclusion`) — ikke kun `null`, som al
eksisterende data giver.

**Bekræftet: `overlapExclusionLabels` (web/src/app/core/labels.ts:78) er endnu helt UBRUGT** — `grep` efter
navnet i hele `web/src` finder kun selve definitionen. Byttemutation (fire værdier roteret) gav 106/106 grønt, som
ventet — ingen komponent importerer den endnu. Ikke en mangel i DENNE PR (opgavebeskrivelsen forudså det selv: "bruges
først i 3c-web"), men skal have en test, DEN dag en komponent renderer den.

**Solidt dræbt derudover** (11/14): `Missing()`s Nedlagt-filter, `InUse()`s Nedlagt-filter, `Coverage.Covered`s
subtraktions-retning (alle tre af netop `Daekningen_er_praecis_...`-testens ÉN metode, tre uafhængige linjer/tal —
Covered:6/Total:8 i kommentaren, IKKE et interval), `none`-filtrets brug af `Missing()` frem for rå `Uncovered` (skal
gøre `Uncovered` midlertidigt `public` for at mutere — husk at rulle SYNLIGHEDEN tilbage sammen med selve mutationen),
`OverlapHoldersAsync`s `GroupKey`(→id) og `Name`(uden forælder)-felter, `GetTree`s hasChildren-argument til
`OverlapOf` (grupper må ikke vurderes — "Grupper vurderes ikke"-assertionen), `CapabilitiesOf`s `SharedWith` med egen
gruppe inkluderet, `SharedWith` sammenlignet på `SystemId` i stedet for `GroupKey`, `SharedWith` ikke tømt når
overlap er null (`En_kobling_der_boer_flyttes_vurderes_ikke`-testen), og `ToDto`s `Members` tømt.

## Generel lektie
`if (false)`/direkte konstant-udkommentering af en gren udløser ofte C# CS0162
("Unreachable code") som fejl (TreatWarningsAsErrors=true i dette repo) og stopper builden
FØR testene kan afsløre noget. Brug i stedet en betingelse compileren ikke kan bevise er
konstant (fx `Environment.TickCount == -1`, eller fjern selve linjen/branchen helt i stedet
for at "slukke" den med en litteral).

**Samme fælde i Angular-templates**: `@if (false) { … c.ownExclusion … }` fejler builden med
TS2531/TS2538 ("Object is possibly 'null'"), fordi typeindsnævringen fra `@if (c.ownExclusion)`
forsvinder, men resten af blokken stadig bruger `c.ownExclusion` unarrowed. Brug i stedet en
runtime-falsk, ikke-compiletids-bevislig betingelse, fx `c.ownExclusion && c.ownExclusion.length < 0`
(bevarer narrowing), i stedet for at erstatte hele udtrykket med `false`.

## Fund d2a134a (delopgave 3c-web, EA-register): overlap og dækning på kortet og systemsiden — meget stærk kerne, ingen blokerende huller

Baseline: `npx ng test --watch=false` (web/, Node 24, `npm install` først i worktree'en) 114/114
i 12 filer. `npm run lint` og `npm run build` grønne. Kørte 13 mutationer i
`capability-map.page.ts/.html` og `system-detail.page.ts/.html` og `system-list.page.html` —
**alle 13 dræbt præcist**, ofte af netop ÉN linje i den relevante nye test:

1. `badges()`s `count(o.counted, 'system', 'systemer')`-argumentrækkefølge byttet (isOverlap-badge) → dræbt
   (`'Overlap · 2 system'` mod ventet `'2 systemer'`, counted=2 i fixturet).
2. Samme bytte på `count(o.planned, 'planlagt system', 'planlagte systemer')` (plannedOnTopOfActive-badge) →
   dræbt (K1.1 planned=1 forventer ental, blev flertal).
3. `memberText()` (kort) uden exclusion-parentes → dræbt (`'Systemer: Kompas, ..., Rune, Ugle'` mod ventet
   `'Rune (planlagt), Ugle (udfases)'`).
4. Dæknings-linjens link-betingelse `t.coverage.covered < t.coverage.total` → `true` → dræbt (linket
   "se de manglende" dukkede op selv ved 8/8 dækket).
5. `attention`-computed'ens `|| n.overlap.plannedOnTopOfActive`-gren fjernet (kun `isOverlap` tilbage) →
   dræbt (K2.1, som KUN har `plannedOnTopOfActive`, forsvandt fra både filter-visningen og
   attention-count).
6. `toggleOverlap()`s `router.navigate(...)`-kald fjernet (kun signalet sat) → dræbt
   (`navigate`-spy'en blev aldrig kaldt — retter IKKE URL'en, kun den lokale tilstand).
7. Initial `overlapOnly`-signal hardkodet til `false` i stedet for at læse
   `route.snapshot.queryParamMap.get('overlap') === '1'` → dræbt (testen, der navigerer direkte til
   `/?overlap=1` og forventer filtreret visning fra start, fanger det).
8. `system-detail.page.ts`s `hasShared()` → altid `false` OG altid `true` (begge retninger) → begge dræbt
   (shared-hint vises/skjules forkert i hver sin test).
9. `system-detail.page.html`s `c.heldBy ? c.heldBy.name : 'Dette system'` → altid `'Dette system'` → dræbt
   (modul-testen forventer `'Løn tæller ikke med...'`, ikke `'Dette system tæller ikke med...'`).
10. `@if (c.sharedWith.length)` → falsk → dræbt (tom "Deles med"-tekst mod ventet indhold).
11. `@if (c.ownExclusion)` → falsk (se narrowing-fælden ovenfor) → dræbt.
12. `system-list.page.html`s NYE `capability-help`-betingelse (linje 76, `&& filter().capabilityId !== 'none'`
    fjernet) → dræbt af den eksisterende `none`-test, som eksplicit forventer `capability-help` er `null`.
13. `attention-count`s `count(overlapCount(), 'kapabilitet', 'kapabiliteter')`-argumenter byttet → dræbt
    (overlapCount=1, forventer ental "1 kapabilitet", fik "1 kapabiliteter").

**Bekræftet ubetinget: `overlapExclusionLabels['Nedlagt']` er utestet i denne PR.** Ændrede værdien til en
tydeligt forkert streng → 114/114 forblev grønt. Alle fixtures i denne PR bruger kun `Planlagt`,
`Udfases` og `LokalLoesning` (kort + systemside). Lav risiko (samme mekanisme, 3/4 værdier ER dækket via
samme opslagstabel — en generel `Record`-mutation ville stadig ramme en dækket værdi), men nævnes til
protokols.

**Bekræftet, PRÆ-EKSISTERENDE, IKKE del af denne PR's diff**: `system-list.page.html:67`s
`@if (filter().capabilityId && filter().capabilityId !== 'none')`-betingelse omkring den ekstra
`<mat-option [value]="filter().capabilityId">` (den, der viser navnet på den valgte, ikke-'none'
kapabilitet) — fjernede kun `&& filter().capabilityId !== 'none'`-halvdelen (beholdt
`@if (filter().capabilityId)`) → **114/114 forblev grønt**. Bekræftet identisk på `origin/main` (samme
linjer, ordret) — IKKE introduceret eller rørt af 3c-web-diffen (kun linje 76's NYE `capability-help`-
betingelse, som bruger samme mønster, ER dækket, se mutation 12 ovenfor). Mekanisme: ved `capabilityId
=== 'none'` ville denne mutation tilføje en ANDEN `<mat-option value="none">` (dublet af den faste
"Ikke angivet"-option lige over), men ingen test tjekker antallet af `<mat-option>`-elementer eller at der
IKKE findes en dubleret 'none'-værdi — kun den viste tekst (`selected(fixture)`), som tilsyneladende
forbliver korrekt uanset dubletten. Hører IKKE til denne PR's dækning (linjen er urørt), men er en reel,
selvstændig test-gæld i `system-list.page.spec.ts` — mangler fx en test, der vælger en kapabilitet, sætter
filteret til `'none'`, og tjekker `querySelectorAll('mat-option').length` (eller fravær af en dubleret
'none'-option), i stedet for kun den synlige tekst.

**Konklusion:** ingen blokerende huller i selve 3c-web-ændringen. Testene rammer indhold (ental/flertal på
tal, EKSAKTE exclusion-labels, ikke bare "der står noget"), bruger rige fixtures (4 kapabiliteter med
forskellige overlap-kombinationer, ikke ét tomt træ), og dækker eksplicit initial `?overlap=1` via
`Router.navigateByUrl` FØR komponenten oprettes. Klar til at lande.

## Fund 9db34ad (delopgave 4a, EA-register): forvaltere redigerer egne systemer — meget stærk kerne
(`SystemAccess`, tre handlere), to reelle huller uden for kernen, én DI-fælde jeg selv skabte

Baseline: `dotnet test --project tests/Ea.Api.Tests` 325/325 (11/11 i `Authorization.StewardAccessTests`), web
`npx ng test --watch=false` 116/116 i 12 filer. Opgavebeskrivelsen havde selv allerede kørt 21 mutationer (18
server + 3 web) og fundet/rettet to dobbelte vagter (nuværende-forælder-genvejen i `ParentCandidates`, og
`mayDelete &&` foran `canDelete`) — jeg kørte 8 uafhængige mutationer for at verificere kernen selv, plus
målrettet på de punkter, jeg blev bedt om at vurdere. **Alle 8 dræbt:**
1. `MoveSystemHandler`s `From == To`-genvej fjernet (efter at forfatteren allerede havde fjernet DENS overflødige
   duplikat i `ParentCandidates`) → dræbt af `Et_modul_kan_ikke_tages_ud_af_en_forælder_forvalteren_ikke_kan_redigere`
   (kandidatlisten skal stadig vise nuværende forælder). Bekræfter: forenklingen (én vagt, ikke to) er reelt dækket,
   ikke bare "ser fint ud".
2. `access.Invalidate()` fjernet fra `UpdateSystem` → dræbt af `Fjerner_forvalteren_sig_selv_viser_svaret_at_hun_ikke_laengere_kan_redigere`
   (samme response viser stadig `CanEdit: true` efter hun har fjernet sin egen rolle i SAMME PUT). Positivt bevist,
   ikke kun "ser rimeligt ud".
3. **`AddScoped<SystemAccess>()` → `AddSingleton<SystemAccess>()` dræbt** af
   `Forvalteren_redigerer_sit_system_og_dets_moduler_men_ikke_andres` linje 92 (`Assert.False(seenByLeo...CanEdit)`)
   — Leo (Reader) arvede Fridas cachede `_editable`-sæt fra en TIDLIGERE request i samme test, fordi en singleton
   deler ÉN instans på tværs af ALLE brugere/requests i hele app-levetiden. God test: samme `TestApp`, flere
   `HttpClient`'er (forskellige brugere), rækkefølgen "forbudt → giv rolle → tilladt → en ANDEN bruger forbudt"
   afslører netop dette. **Mønster at genkende næste gang**: en cache pr. request (`AddScoped`) bevises IKKE af at
   den samme bruger ser korrekt opdaterede data (det kunne også ske med en singleton, der aldrig cacher forkert for
   ÉN bruger ad gangen) — det kræver en test med MINDST TO forskellige identiteter mod samme kørende instans, hvor
   den ene ikke må se den andens tilstand.
4. "Modul giver ikke ret over forælder" (bug: tilføjede `parents`-opslag i `SystemAccess.EditableAsync`, så en rolle
   på et modul også gav ret over dets forælder) → dræbt AF TO tests på én gang
   (`En_rolle_paa_et_modul_giver_ikke_ret_over_forælderen` og `Et_modul_kan_ikke_tages_ud_af_en_forælder...`).
   Bekræfter empirisk (ikke kun ved læsning), at "en forvalter på et modul redigerer forælderens integrationer" IKKE
   er et separat hul: `SystemAccess` er fælles for `EditSystemHandler`/`EditIntegrationHandler`/`MoveSystemHandler`,
   så én vagt dækker alle tre forbrugere (præcis det mønster CLAUDE.md efterspørger: "Én vagt pr. sikkerhedsregel,
   samlet ét sted").
5. `system-form.page.ts`s `getRawValue()` → `.value` (dropper disabled `parentSystemId`) → dræbt af den NYE test
   "et låst forælder-felt sender den nuværende forælder med" (ikke af en ældre test — bekræfter den nye test
   reelt tilføjer dækning, ikke bare gentager en gammel).
6. `canManagePersons` hardkodet til `true` → dræbt af den NYE test "en forvalter, der ikke må oprette personer,
   får at vide, hvem der kan" (begge retninger: hint TIL STEDE ved `me(false)`, VÆK ved `me(true)`).

**To reelle huller (verificeret ved mutation, IKKE dræbt af suiten):**

1. **`SystemEndpoints.ParentCandidates`s `canCreate`-gate (kun admin må se kandidater til et NYT system) er
   utestet for ikke-admin.** `src/Ea.Api/Systems/SystemEndpoints.cs`: `var allowed = system is null ? canCreate : …`
   — ændrede til `system is null ? true : …` → 325/325 forblev grønt. INGEN test kalder
   `GET /api/systems/parent-candidates` (uden `forSystemId`) som andet end admin — den eneste test af dette endpoint
   uden `forSystemId` (`Forælder_kandidater_foelger_samme_regel_som_gem` i `SystemEndpointsTests.cs`) bruger kun
   `admin`. Reel konsekvens: en forvalter (eller læser), der åbner "opret nyt system"-formularen, ville se FULDE
   forælder-kandidatliste, selvom hun slet ikke må oprette systemer (kun admin må) — ikke en skriveret, men en
   informationslæk om systemtræet + en vildledende formular (viser valg, hun aldrig kan gemme). Mangler: en test
   der kalder `parent-candidates` UDEN `forSystemId` som `TestUsers.Steward` (eller `Reader`) og forventer en TOM
   liste.
2. **`DevSeed`s binding af "Frida" (dev-login-brugeren, `FridaOid`) til en Person med Systemforvalter-rolle på
   "Nordlys ERP"/"Laborant" — selve broen, der lader en RIGTIG udvikler logge ind som "frida" og opleve 4a-featuren
   — er helt utestet.** `src/Ea.Api/Data/DevSeed.cs:19` (`FridaOid`-konstanten, matcher
   `appsettings.Development.json`s dev-login-bruger "frida") ændret til en anden guid → `DevelopmentStartupTests`
   (2/2) OG hele suiten (325/325) forblev grøn. Ingen test logger ind som "frida" mod DevSeed-data og tjekker
   `CanEdit`/rolle. `StewardAccessTests` bruger sin EGEN `TestUsers.Steward` (anden oid, `TestAccess.BindPersonAsync`
   mod frisk testdata) — dette dækker REGLEN glimrende, men IKKE at DevSeed/appsettings-parret faktisk hænger
   sammen. Mangler: en test i `DevelopmentStartupTests.cs`, der logger ind som `frida` (`TestUsers`-post med samme
   oid som `DevSeed.FridaOid`, eller genbrug af selve konstanten) og bekræfter `CanEdit: true` på "Nordlys ERP".

**Migreringen (`PersonOid`, RenameColumn) — verificeret EMPIRISK, ikke kun ved læsning, at eksisterende data
overlever:** ved et selvforskyldt uheld (forkert env-var-navn: `ConnectionStrings__EaDb` i stedet for
`ConnectionStrings__Ea`) kørte `dotnet ef database update Koblinger` faktisk mod den lokale `ea_dev` (som allerede
havde 6 personer, inkl. Frida med `entra_object_id = …f001`) i stedet for en frisk scratch-database, og reverterede
`PersonOid`-migrationen dér. Genanvendte migrationen fremad (`dotnet ef database update`) og bekræftede at Fridas
værdi lå UÆNDRET under det nye kolonnenavn `oid` bagefter — `RenameColumn` er metadata-only i PostgreSQL, ingen
datatab. `ea_dev` er nu tilbage i korrekt migreret tilstand. **Ingen automatiseret test dækker dette** (testene
migrerer altid en TOM skabelon fra bunden, aldrig en eksisterende databases opgraderingssti — konsistent med alle
tidligere migreringer i projektet, ikke nyt for 4a), men risikoen er reelt lav (ren omdøbning, ingen datatransform).
**Lektie til næste gang**: `dotnet ef`-kommandoer mod en specifik scratch-database KRÆVER `ConnectionStrings__Ea`
(præcis navnet fra `DatabaseSetup.ConnectionName`), ikke et gættet variabelnavn — ellers rammer kommandoen
appsettings.Development.json's `ea_dev` uden varsel. Tjek altid `psql -d postgres -c "\l"` FØR og EFTER en
`dotnet ef database update` mod en formodet scratch-database, hvis man ikke er 100% sikker på env-var-navnet.

**Vurderet, men IKKE et hul (bevidst ikke rapporteret som mangel, kun som lav-værdi-observation):**
`canAdd` i `SystemIntegrationsResponse` og bekræftelse (`ConfirmSystem`) af et modul specifikt er ikke separat
assertet for et MODUL (kun for parent-systemet `w.P`), men begge genbruger PRÆCIS samme `EditSystemHandler`/
`SystemAccess.CanEditAsync`-mekanisme, som ER dræbt-testet med modul-inheritance (`EditAsync(w, w.M, …)`). En
mutation isoleret til "modul-specifik" adfærd findes ikke uden at røre selve `SystemAccess` (allerede dækket) eller
opfinde en kunstig bug, der ikke findes i koden. Samme ræsonnement for "integration hvor begge ender er moduler
under hendes system" (OR af to allerede uafhængigt dræbte prædikater).

## Fund c6e9bed (SQL-hærdning, PR #15): stærk kerne, ÉT reelt hul i selve vagten (bypass), resten dræbt

Baseline: `dotnet build EA.slnx` og `-c Release` begge 0/0 grønt; fuld suite `dotnet test --project
tests/Ea.Api.Tests` 335/335 grønt. Alle mutationer kørt mod committet `c6e9bed` i egen worktree, genskabt med
`git checkout -- <fil>` mellem hver.

**BLOKERENDE HUL: `SqlSafetyTests`s tekst-scanning (`RawSqlApis`/`SqlApis`-listerne) kan omgås af
`NpgsqlDataSource.CreateCommand(sql)`, og INGEN af de tre analyzere (EF1002/CA2100/CA3001) fanger den heller.**
Tilføjede `src/Ea.Api/Data/DataSourceScratch.cs` med præcis dette (Npgsql er allerede en transitiv afhængighed via
`Npgsql.EntityFrameworkCore.PostgreSQL`, så INGEN ny pakke krævet):
```csharp
public static async Task RunAsync(NpgsqlDataSource dataSource, string table)
{
    await using var cmd = dataSource.CreateCommand("SELECT * FROM " + table); // klassisk konkatenering
    await cmd.ExecuteNonQueryAsync();
}
```
`dotnet build EA.slnx -c Release` → **0 Warnings, 0 Errors** (analyzerne tavse — deres sink-signaturer kender kun
`DbCommand.CommandText`-SETTEREN og EF's `*Raw`/`FromSql*`-metoder, ikke `NpgsqlDataSource.CreateCommand(string)`,
som tager SQL-teksten som KONSTRUKTØR-lignende ARGUMENT). `dotnet run --project tests/Ea.Api.Tests --
-class "Ea.Api.Tests.Data.SqlSafetyTests"` → **5/5 grønt, uændret** (ordet "NpgsqlDataSource" og "CreateCommand"
står ikke i `RawSqlApis`/`SqlApis`). Kontrast: samme forsøg via `Database.GetDbConnection().CreateCommand()` +
`cmd.CommandText = "..." + table` FANGES fint (CA2100-byggefejl OG tekst-scan, fordi "CommandText" er en
scannet streng) — det er specifikt API'er, der tager SQL som ARGUMENT (ikke en settable property), der er blinde
vinkler. Samme kategori (utestet, ikke forsøgt pga. ingen NuGet-adgang i denne kørsel): Dapper (`connection
.Query<T>(sql)`) — bruger heller ikke `CommandText`-setteren og er ikke i nogen af listerne. **Forslag:** tilføj
"NpgsqlDataSource", "CreateCommand", "Dapper" til `RawSqlApis`, ELLER (bedre, mere robust) vend testen om: scan
efter TILLADTE mønstre (kun EF Core LINQ + `TableLocks.Sql`) i stedet for FORBUDTE navne — en positiv liste kan
ikke omgås af et nyt API-navn, en negativ kan. Nævn til ejeren/Quality Control: CodeQL (som ikke kan køres lokalt)
er den eneste tilbageværende chance for at fange denne klasse, og `security-extended`-CodeQL-pakken har
formodentlig sink-modeller for både Npgsql og Dapper, men det er IKKE efterprøvet her.

**Dræbt, som forventet:** rå SQL i `CsvImport.cs` via `ExecuteSqlRawAsync("SELECT 1", ct)` (konstant streng, ingen
interpolation — analyzerne tavse, men tekst-scanningen fangede `ExecuteSqlRaw` alligevel: `API_et_bruger_ingen_raa_SQL`
rød med `["Common/CsvImport.cs: ExecuteSqlRaw"]`); parametriseret `ExecuteSqlAsync($"SELECT 1", ct)` uden for
`TableLocks.cs` (`SQL_tekst_findes_kun_i_den_faste_laaseliste` rød: `["Data/TableLocks.cs", "Common/CsvImport.cs"]`);
`CommitIfUnchangedAsync`s `LockAsync`-kald fjernet (`_ = tableLock;`) → 2/51 røde i de rigtige PostgreSQL-race-tests
(importen tager reelt ikke låsen længere); en forkert låsetekst (`TableLock.Couplings` ændret til at låse
`ea.systems` i stedet for `ea.system_capabilities`) → øjeblikkeligt rød på enheds-niveau
(`Hver_laas_er_en_konstant_tekst_uden_argumenter`, forventet vs. faktisk streng), OG 4/51 røde i de ægte
race-tests (formular-PUT og sletning bruger begge `TableLock.Couplings` og enten låser forkert eller rammer en
uventet `55P03`-lock-fejl, fordi timings-antagelsen i `WaitForBlockedLockAsync`/`FOR UPDATE NOWAIT` brydes).

**Spørgsmål "deler race-testene nu blindt produktionens låsetekst?" — svaret er NEJ, af to grunde.** (1)
`SqlSafetyTests.Hver_laas_er_en_konstant_tekst_uden_argumenter` er en UAFHÆNGIG Theory med hårdkodet
forventet streng pr. enum-værdi — den fanger enhver tekst-mutation på millisekund-niveau, uanset om race-testene
bruger samme kilde. (2) Selv når race-testene bevidst kalder `TableLocks.Sql(...)` (samme kilde som
produktionen), afslørede en forkert tabel STADIG 4 ægte PostgreSQL-race-tests, fordi disse tests' antagelser
(`WaitForBlockedLockAsync`s faste standard-tabel `ea.system_capabilities`, og "importen kan stadig FOR UPDATE
NOWAIT-låse systemrækken") er uafhængige af selve låseteksten og brydes, når den ændrer sig. Refaktoreringen
(fra to uafhængige hårdkodede strenge til delt `TableLocks.Sql`) reducerer duplikering uden reelt tab af dækning
— MEN hvis `Hver_laas_er_en_konstant_tekst_uden_argumenter` nogensinde fjernes, mistes den hurtige, garanterede
fangst, og man er tilbage til kun at stole på de dyrere, indirekte race-test-symptomer.

**`Alle_laase_har_en_tekst` er IKKE en dobbelt-vagt af Theory'en — den dækker noget andet (enum-exhaustiveness).**
Tilføjede et tredje `TableLock`-medlem (`Scratch`) UDEN switch-case i `TableLocks.Sql` (rammer kun default
`_ => throw`) → Theory'en (kun 2 `[InlineData]`) forblev upåvirket/grøn (rører aldrig den nye værdi), men
`Alle_laase_har_en_tekst` (som itererer `Enum.GetValues<TableLock>()` via refleksion) blev rød med
`ArgumentOutOfRangeException: Ukendt lås.` — den fanger specifikt "ny lås tilføjet, glemt i switch'en", en fejl
Theory'en aldrig ville opdage, fordi den kun kører de værdier, forfatteren huskede at nævne.

**Editorconfig-sektionen `[src/**/*.cs]` er bevist AKTIV i Release-build (CI's faktiske kommando
`dotnet build EA.slnx -c Release`), for alle tre regler, uden nogen særlig `AnalysisMode`:** lagde en
interpoleret `ExecuteSqlRawAsync($"...")` ind i `src/Ea.Api/Data/TableLocks.cs` → EF1002-byggefejl. Lagde
`NpgsqlCommand.CommandText = "..." + variabel` ind samme sted → CA2100-byggefejl. Lagde en minimal-API-handler
med `request.Query["table"]` direkte i en `new NpgsqlCommand(...)`-konstruktør ind → BÅDE CA2100 og CA3001
(taint fra `HttpRequest.Query` til SQL-sink) fejlede bygget — CA3001 kræver altså IKKE en særlig `AnalysisMode`
her, kun at severity sættes i `.editorconfig` (`AnalysisLevel=latest-recommended` i `Directory.Build.props` er
nok til at analyzer-pakken er med; editorconfig-severity slår den til). Negativ kontrol: `tests/Ea.Api.Tests/
Infrastructure/DbLocks.cs` bruger PRÆCIS det mønster (`new NpgsqlCommand(sql, connection, transaction)` med en
vilkårlig `sql`-parameter), der ville udløse CA2100 — bygget er 0/0 grønt i dag, hvilket bekræfter at
`[src/**/*.cs]`-scopet IKKE rammer `tests/` (som tilsigtet, jf. kommentaren i .editorconfig).

**Ikke undersøgt i denne omgang (uden for komiteret kode, men nævnt til ejeren):** "mindste rettighed i
databasen" (`ea_app`/`ea_migrator`-rolleadskillelse) er KUN dokumenteret som beslutning X i `docs/plan.md` —
INGEN kode i denne PR opretter rollerne eller ændrer connection strings; det er eksplicit udskudt til F3/F4
("Rollerne oprettes af ejerens script i F3/F4"). Ikke en Test Manager-mangel (intet at mutere endnu), men
Quality Control/Release Manager bør vide, at ejerens fjerde krav ("mindste rettighed i databasen") kun er en
PLAN, ikke en implementering, i denne PR.

## Fund 17b36ba (delopgave 4b-1, EA-register, PR #16): "Mine systemer", menuens tal, editors — exceptionelt stærk
kerne (18 mutationer kørt, 17 dræbt), ét reelt hul (navnetie-break) + én "svaghed i bevisførelsen" (implicit DB-orden)

Baseline: `dotnet test --project tests/Ea.Api.Tests` 342/342 (42/42 i `Ea.Api.Tests.Systems`, ny fil
`MySystemsTests.cs` med 5 facts). Web `npx ng test --watch=false` (kørt som
`/opt/node24/bin/node ./node_modules/.bin/ng test --watch=false` efter `/opt/node24/bin/npm install`) 137/137 i 13
filer. Opgavebeskrivelsen selv havde allerede kørt 20 mutationer (10 server + 10 web) og fundet dem alle røde — jeg
kørte 18 uafhængige mutationer for selv at verificere kernen (`SystemQueries.Mine`, `OidOrNull`, sorteringen,
`EditorsOf`, `canEditViaParent`, `/api/me`, samt web: `myRole`, toggle-aria, URL-følge-logik, begge
system-form-advarsler, `loadMe`, editors-teksten, tom-teksten, `landingUrl`s `/systemer`-særtilfælde) — **17 af 18
dræbt**, ofte af netop ÉN linje i den navngivne test:

1. `SystemQueries.Mine`s modul-arv fjernet → dræbt (`Mine_systemer_er_alle_roller...`, "Ejer-modul" forsvinder).
2. `Mine`s rolle-filter indsnævret til kun `EditingRoles` (ekskluderer Forretningsejer) → dræbt (samme test,
   "Ejer-system"/"Systemejer-system" ville miste deres eneste rolle).
3. `OidOrNull` accepterer blankt oid (fjernet `IsNullOrWhiteSpace`-tjekket) → dræbt (3 fejl i
   Systems+Authorization, inkl. `Uden_egne_roller_eller_uden_identitet_er_der_ingen_mine_systemer`s tomme-oid-case).
4. Sortering: nedlagte IKKE sidst (fjernet `OrderBy(Nedlagt)`) → dræbt.
5. Sortering: nyeste først (`ThenByDescending` i stedet for `ThenBy` på `LastConfirmedAt`) → dræbt.
6. **Navnetie-break i Mine-sorteringen (`ThenBy(NameNormalized)` → `ThenByDescending`) → 42/42 forblev GRØNT.**
   Se hul-afsnittet nedenfor.
7. `EditorsOf` uden forælderens redaktører (`s.ParentSystem is null || true`) → dræbt.
8. `EditorsOf` med Forretningsejer tilføjet til `EditingRoles` → dræbt (Dan optræder ikke i nogen forventet liste).
9. `EditorsOf`s dedup fjernet (`own.All(o => o.Person.Id != p.Id)`-filteret) → dræbt (Cille ville optræde to gange).
10. `canEditViaParent` hardkodet `false` → dræbt (`Ret_via_forælderen_saettes_af_serveren`).
11. `/api/me`s `PersonId` hardkodet `null` → dræbt.
12. Web `myRole()`s via-forælder-fald (`Via ${item.parent?.name}`) → fast streng `'–'` → dræbt.
13. Web toggle-knappens `[attr.aria-pressed]="mine()"` → `false` → dræbt.
14. Web: `ngOnInit` ændret fra `queryParamMap.subscribe` til én gangs `route.snapshot`-læsning (listen følger ikke
    længere URL'en) → dræbt (`et link til "Mine systemer" på samme side henter listen igen`-testen fanger det
    som en MANGLENDE HTTP-forespørgsel, en stærkere fejlsignatur end en assertion).
15. `selfRemovalWarning`s `s.permissions.canEditViaParent`-guard fjernet → dræbt.
16. `noEditorsWarning`s `s.parent`-guard (modul-undtagelsen) fjernet → dræbt.
17. `system-form.page.ts`s `loadMe()`-kald efter gem fjernet → dræbt (3 tests, ny "uventet forespørgsel"-fejl —
    testene forventer eksplicit `http.expectOne('/api/me')` efter hver gem).
18. `system-detail.page.html`s "Redigeres af"/"Ser noget forkert ud?"-tekst byttet → dræbt.
19. `system-list.page.html`s tom-tekst byttet ("Ingen af dine..." ↔ "Du har ingen rolle...") → dræbt.
20. `landingUrl`s særtilfælde for `/systemer` som IKKE-mål fjernet (`target && target !== '/systemer'` →
    kun `target`) → dræbt (`standardadressen er ikke et mål`-testen).

**Ét reelt hul: navnetie-break i Mine-sorteringen er UBEVIST.** `SystemEndpoints.cs`, mine-grenens
`.ThenBy(s => s.NameNormalized)` (efter `LifecycleStatus == Nedlagt` og `LastConfirmedAt`) — byttet til
`ThenByDescending` → **42/42 forblev grønt**. Årsag: `Mine_systemer_er_alle_roller_og_deres_moduler...`s fem
systemer får hver sin egen dag via `app.Time.Advance(TimeSpan.FromDays(1))` mellem oprettelserne, så
`LastConfirmedAt` ALDRIG er ens for to rækker — tie-break'en på navn bliver aldrig aktiveret. Mangler: to systemer
med SAMME `LastConfirmedAt` (fx begge oprettet/bekræftet i samme øjeblik, uden `app.Time.Advance()` imellem) og
forskellige navne, der bekræfter den alfabetisk først navngivne kommer FØRST i `mine`-listen.

**"Svaghed i bevisførelsen" (ikke en reel mangel i selve reglen, men i testens evne til at bevise den):**
`myRoles?[r.Id].Distinct().Order().ToList()`s `.Order()`-kald (alfabetisk/enum-rækkefølge på "Din rolle",
fx "Forretningsejer, Systemejer") — fjernede `.Order()` helt → **42/42 forblev grønt**, MEN byttede til
`.OrderDescending()` → dræbt (1 fejl). Årsag: `SystemRoleAssignment`s sammensatte primærnøgle er
`(SystemId, Role, PersonId)` (`EaDbContext.cs:90`), så PostgreSQL's indeks-scan for `WHERE SystemId IN (...)`
tilfældigvis returnerer rækkerne allerede i Role-rækkefølge — uden nogen eksplicit `ORDER BY` fra koden. Koden er
IKKE forkert (den skriver eksplicit `.Order()`, hvilket er den rigtige, robuste løsning), men testen beviser reelt
kun retningen (asc vs desc), ikke at `.Order()`-kaldet SELV er nødvendigt — en udvikler, der ved et uheld fjerner
`.Order()` helt (fx under en refaktorering), ville ikke få en rød test, fordi Postgres' udførelsesplan tilfældigvis
matcher. **Mønster at genkende næste gang**: når en sortering på et LINQ-resultat af en DB-forespørgsel uden
eksplicit sortering "tilfældigvis" matcher forventningen efter en mutation, mistænk at den sammensatte primærnøgle/
et indeks giver en ufrivillig men konsistent fysisk rækkefølge — test dette specifikt ved at BYTTE
retningen (asc↔desc), ikke kun ved at FJERNE sorteringen, for at skelne "ubevist" fra "reelt forkert i denne
kørsel, ville måske fejle i en anden Postgres-version/plan".

**Strukturelt bekræftet, ikke et hul:** `EditorsOf` (permissions-visning) og `SystemAccess` (den faktiske
adgangskontrol) refererer BEGGE `SystemRules.EditingRoles` som samme statiske felt — ikke to hardkodede lister,
der kan glide fra hinanden med grøn suite. Der er derfor ingen reel "to vagter om samme regel"-risiko her (CLAUDE.
dks bekymring), fordi det strukturelt er ét sted. Der findes dog ingen DIREKTE test, der binder "en person i
Editors-listen" sammen med "denne person ser CanEdit: true, når hun selv logger ind" (kun `canEditViaParent`
er testet med brugerens egen klient, ikke listens øvrige medlemmer) — lav værdi at tilføje, given den delte
kildekode, men nævnt for fuldstændighedens skyld.

**Bekræftet, ikke testet i denne PR (lavt-risiko gap, nævnt men ikke blokerende):** `mine=true` kombineret med
fritekstsøgning (`q`) er UDEN test, hverken server (`MySystemsTests.cs`) eller web
(`system-list.page.spec.ts`, som kun kombinerer `mine` med `status`). Koden komponerer blot endnu et `.Where()`
oven på `query.Mine(user)` (samme generiske mønster som alle andre filterkombinationer, der ER testet), så risikoen
er lav — men "mine sammen med søgning" er en oplagt bruger-adfærd (en forvalter, der søger blandt SINE systemer),
som ingen test dækker eksplicit.

**Ikke en mangel, men værd at bemærke:** `web/src/app/login/login.page.ts` (som nu kalder `landingUrl(...)`) har
STADIG ingen `.spec.ts`-fil overhovedet — hverken før eller efter denne PR. `landing.ts` selv er grundigt testet
som en ren funktion (`landing.spec.ts`, 4 tests, dræbte to selvstændige mutationer ovenfor), så login-siden er kun
en tynd sammenkobling (samme lav-risiko-mønster som utestede routes i tidligere delopgaver) — ikke PR-specifikt,
men konsistent test-gæld.

**Konklusion: KLAR TIL AT LANDE.** Kernen (server: `Mine`, `OidOrNull`, sortering, `EditorsOf`, `canEditViaParent`,
`/api/me`; web: menu-eksklusivitet, URL-følgende liste, begge advarsler, `loadMe`, editors/tom-tekst,
`landingUrl`) er exceptionelt grundigt bevist — markant stærkere end gennemsnittet, fordi opgavebeskrivelsens egen
mutationsliste (20 mutationer) allerede var kørt og lukket FØR denne gennemgang. Det ene reelle hul
(navnetie-break) er lav-risiko (kræver to systemer bekræftet i PRÆCIS samme øjeblik) og ikke blokerende alene,
men bør noteres som en manglende test, ikke rettes hastigt uden en ny test der beviser den.
