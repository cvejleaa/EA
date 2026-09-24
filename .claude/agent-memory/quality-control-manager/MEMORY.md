# Quality Control — fælder, invarianter, tekst der lover for meget

## Projekt
EA-register "LeanIX-light" for ITFL/DTU. Brugere: EA (kurator/admin), travle systemforvaltere, teamledere, sikkerhed.
Plan 2026-09-24: 1 register, 2 integrationer+CSV, 3 kapabiliteter+overlap, 4 forvaltere+audit, 5 teknologi/EOL,
6 Excel-import, 7 SOP, 8 kontrakter.

## Plan-gennemgang 1 (2026-09-24) — fund, status efter kode-gennemgang af d646a21 (delopg. 1)
- Navneunikhed globalt vs. moduler → LØST: unikt indeks (ParentSystemId, NameNormalized), AreNullsDistinct(false).
  Topniveau-navne unikke globalt, modulnavne kun unikke inden for samme forælder. Listen viser "Forælder › Modul".
- "Udfases"/"Udfaset" kollision → LØST: endte på "Nedlagt" (SystemEnums.cs). Enum-navne er dokumenteret ekstern
  kontrakt (omdøbes aldrig; nye værdier tilføjes).
- Default-status + huller → LØST: ingen default (status er tvunget aktivt valg i formularen), filtre "Ikke
  angivet" på team OG forretningsejer, både i UI og server (SystemEndpoints.None = "none").
- "Ansvarligt team" vs. systemejer-rollen → LØST: feltet hedder "Forvaltende team" (ManagingTeam), adskilt fra
  rollen "Systemejer" i UI og model. Ingen kollision i screenshots.
- Slet vs. Udfaset → LØST: slet-knappen er disabled med forklaringstekst ved siden af (deleteBlockedReason fra
  serveren), ingen 409-efter-klik. Bekræftelsesdialog nævner eksplicit "sæt status til Nedlagt i stedet".
- Forælder-vælger kun gyldige forældre → LØST: /parent-candidates bruger samme SystemRules.ValidateParent som
  gem-vejen (ét sted, ikke to vagter).
- Point 3, 4 (overlap, importnøgle) er ikke reelevante for delopg. 1 — udestår til delopg. 2/3.

## Ny plan-tilføjelse midt i delopg. 1: forretningsejer/systemejer/systemforvalter-roller rykket frem
Design: SystemRole-enum (Forretningsejer, Systemejer, Systemforvalter), Person (DisplayName/Email/Department),
SystemRules.SingleHolderRoles = {Forretningsejer, Systemejer} håndhævet ÉT sted og brugt af både Apply() og
klientens formular (single-select vs. multi-select for forvaltere). Liste viser forretningsejer + afdeling,
filtrerbar "Ikke angivet". Vurderet GOD: løser "forretningsvinklen" uden at foregribe delopg. 4 (adgang for
forvaltere er stadig ikke implementeret — EditSystemHandler tillader kun EA.Admin, med kommentar om at delopg. 4
udvider den). Ingen modsigelse fundet mellem rolleliste og andre tal på samme skærmbillede.
- ÅBEN/svag: SingleHolderRoles håndhæves kun i applikationskoden (Apply/ValidateRoles), ikke som DB-constraint.
  To samtidige PUT'er kunne i teorien begge validere før nogen commit'er og give 2 forretningsejere. Lavt
  praktisk risiko nu (kun admin redigerer), men bliver relevant i delopg. 4 når flere forvaltere får skriveadgang
  samtidig — spørg dengang om der skal en unik, delvis DB-constraint til (WHERE role IN (...)).
- Forretningsejer-filteret i UI tilbyder kun "Alle"/"Ikke angivet", ikke en konkret person (serveren understøtter
  businessOwnerId=<guid>). Bevidst scope-afgrænsning, ikke et brudt løfte — ingen knap/link peger derhen endnu.

## Generelle spørgsmål til dette projekt
- Beregnes knap-synlighed (canEdit/canCreate/canDelete) af serveren, eller sammenligner klienten rollestrenge?
  → I delopg. 1: JA, server (MePermissions + SystemPermissions), klienten læser kun permissions-feltet.
- 409 ved samtidig redigering: dansk besked, og mister brugeren sine indtastninger?
  → Løst: xmin-baseret (Version), dansk problem-besked, "Hent nyeste version (dine ændringer kasseres)"-knap;
  indtastningerne bevares i formularen indtil brugeren selv vælger at genindlæse.

## Invarianter at følge ved fremtidige delopgaver
- SystemRules.cs er ENESTE sted for forretningsregler; både endpoints og `permissions`-beregningen (ToDetail)
  bruger den. Tjek ved hver ændring, at ingen ny regel dukker op dobbelt (klient + server).
- Enum-værdier (SystemEnums.cs) er ekstern kontrakt — labels ligger i web/src/app/core/labels.ts som en
  `Record<Enum, string>`, så en ny værdi giver kompileringsfejl i klienten indtil den har et dansk navn. God
  mekanisme — tjek at nye enum-værdier i fremtidige PR'er faktisk rammer begge filer.
- Seed-data (DevSeed.cs) er konsekvent mærket "(fiktiv)" og bruger @eksempel.invalid — brug som skabelon for
  fremtidige seeds.

## Plan-gennemgang delopg. 2 (2026-09-24) — integrationer + CSV. Tjek ved kode-gennemgangen, om disse blev taget
- xmin-FÆLDE: EF sender kun concurrency-tjek, når selve rækken UPDATEs. Systemer klarer det via Touch() (UpdatedAt
  sættes altid). En entitet UDEN tidsstempler, hvor en PUT kun ændrer M:N (dataobjekter), får INGEN Version-kontrol
  → tavs last-writer-wins. Spørg altid: hvad tvinger rækken til at blive skrevet ved en ren relationsændring?
- 409 er i dag ens for dublet og forældet version (Problems.Conflict/StaleVersion, begge title "Konflikt", ingen
  type). Klienten kan ikke skelne → "Hent nyeste version" vises også ved dublet. Serveren skal mærke dem forskelligt.
- "Bekræft uændret"/"Sidst ændret" dækker kun systemrækken. Står en ny sektion (integrationer) OVER knappen, tror
  brugeren, at den er bekræftet med. Spørg ved hver ny sektion på systemsiden: dækker friskheden den?
- CSV som ekstern kontrakt: modul-navne er kun unikke inden for forælder → visningsnavn alene er tvetydigt; navn vs.
  id-kolonne: hvem vinder ved uenighed; formel-neutralisering (') skal kunne rulles tilbage ved import; "Ikke angivet"
  = tom celle; rækker der mangler i filen må ikke slettes; systemer der ikke findes i registret (foranalysen FINDER
  lister/udtræk!) — hvad skriver de?
- Naturlig nøgle (fra,til,type,via) vs. ExternalKey: to eksterne id'er kan kollapse til én række → import ikke
  idempotent via ExternalKey. Tjek i delopg. 6.
- "udtræk" er både integrationstype (plan.md) og systemtype (LokalLoesning "Lokal løsning/udtræk") — kræver en opskrift.
- Retning = dataflow (API-pull B→A registreres B→A) er modintuitivt for teknikere → kolonnenavne/formular skal sige
  "data" eksplicit, og fra/til kan ikke rettes (B) → fejl koster slet+genopret.

## Kode-gennemgang delopg. 2 server (a0caef1, PR #2, branch claude/trusting-brahmagupta-4v2ukj) — status på plan-fund
Konklusion: GOD AT LANDE. Alle fem plan-fund fra 2026-09-24 er håndteret i koden, ikke kun i planen:
- xmin-fælden LØST: Integration.UpdatedAt sættes ved HVER skrivning (kommentar forklarer hvorfor: uden det ville
  en ren dataobjekt-ændring (M:N) ikke tvinge en UPDATE af selve rækken, og Version/xmin ville aldrig blive
  tjekket). Samme mønster som SystemEntity — genbrugt korrekt.
- 409-skelnen LØST: Problems.cs har type (stale-version/duplicate/blocked) på alle konflikter (også systemets
  navne-dublet og slet-blokering, som blev rettet FRA generisk Conflict TIL Duplicate/Blocked i samme commit).
  Klienten (system-form.page.html) viser nu "Hent nyeste version" kun ved `p.type === staleVersion`, ikke
  `p.status === 409` — grebet er flyttet fra status til type, som planlagt.
- Friskhedens rækkevidde: relevant for UI, som endnu ikke er bygget (denne PR er server + skabelon). Ingen ny
  sektion på systemsiden endnu — spørgsmålet er stadig åbent til D3/D4, når integrationssektionen bygges: skal
  den stå UNDER "Bekræft uændret", eller skal friskheden udvides til at dække den?
- CSV-kontrakt-tjeklisten LØST solidt: CsvTemplateTests.cs har to guard-tests — (1) golden-fil == det eksporten
  skriver (UPDATE_CONTRACT=1), (2) vejledningen nævner hver kolonne OG hver typekode (Assert.Contains per værdi,
  ikke bare "findes en tabel"). "Udtræk er både type og systemtype"-forvirringen har fået sin egen opskrift i
  doc'en (afsnit "Lister, regneark og datatræk, der ikke findes i registret": 1) registrér listen som et system
  af typen Lokal løsning/udtræk, 2) flowet dertil som Type=Udtraek). God model at genbruge til fremtidige
  eksterne kontrakt-filer: golden-fil-lighed + "nævner alle enum-koder" i to separate tests.
- Naturlig nøgle vs. ExternalKey: STADIG ÅBEN, bevidst — importen er ikke bygget endnu (delopg. 6). Doc'en
  beskriver allerede den planlagte matchrækkefølge (Id → ExternalKey → fire felter+Navn), hvilket er en
  fremtids-kontrakt, ikke en nutidig funktion — klart markeret med en boks øverst i docs/csv-integrationer.md
  ("Import kommer i en senere delopgave"). Vurderet OK (ikke "lover for meget"), men tjek ved delopg. 6, om
  matchrækkefølgen rent faktisk løser kollisionen mellem to ExternalKeys, der peger på samme naturlige nøgle.

## Nye mønstre/fælder fundet i denne gennemgang
- Delete-blokering udvidet fra kun "har moduler" til også "indgår i integrationer"/"er platform for integrationer"
  via SystemRules.DeleteBlockedReason(name, SystemUsage) — ét sted, brugt af DeleteSystem OG ToDetail (permissions).
  God model: en ny afhængighedstype til et eksisterende "kan ikke slette"-koncept skal udvide SAMME funktion/type
  (SystemUsage-record), ikke en ny sideløbende regel.
- IntegrationSummary blander enheder på samme DTO: Receivers/Suppliers/LocalSolutions tæller FORSKELLIGE SYSTEMER
  (distinct), men ViaPlatform/DirectDb tæller INTEGRATIONER (rækker). Er tydeligt dokumenteret i XML-kommentaren
  pr. felt, så ikke en modsigelse — men når UI'et (D3/D4) viser en tællelinje, så tjek at teksten ikke antyder
  samme enhed for alle fem tal ("3 modtagere, 2 direkte databaseadgange" er fint; "3 modtagere, 2 platforme" hvor
  det ene er systemer og det andet integrationer, er ikke).
- To forskellige sorteringer for "samme" data: systemsidens liste (ForSystem: relation → modtager-navn → type)
  vs. CSV-eksport for ét system (IntegrationCsv.Write: fra-navn → til-navn → type → navn), begge kalder dog
  IntegrationQueries (fælles UDVALG/familie). Ikke en fejl — de er forskellige visninger med forskelligt formål —
  men spørg ved D3/D4, om en bruger, der eksporterer fra systemsiden, undrer sig over anden rækkefølge end skærmen.
- Nyt enum tilføjet (IntegrationType, IntegrationRelation) UDEN tilsvarende Record i web/src/app/core/labels.ts
  endnu — bevidst, fordi UI-sektionen kommer i en senere PR. IKKE en fejl nu (ingen kode refererer til dem endnu,
  så TS-kompileringen fanger ikke noget), men er en åben opgave: husk labels.ts, når D3/D4 bygger integrations-UI.
- CSV FormulaGuard dækker flere tegn (=,+,-,@,TAB,CR) end vejledningens tekst nævner (=,+,-,@) — vejledningen er
  en bevidst forenkling for mennesker, ikke en kontraktafvigelse (koden er en superset), men tjek ved ændringer
  i Csv.cs, at vejledningen ikke bliver direkte forkert (ikke kun ufuldstændig).

## Kode-gennemgang delopg. 2 web/UI (36c0817, PR #3, branch claude/trusting-brahmagupta-4v2ukj)
Konklusion: GOD AT LANDE, med én BØR-rettelse (ikke blokerende). Alle plan-fund fra "Plan-gennemgang delopg. 2"
er håndteret i UI'et:
- Retning=dataflow-fælden: formularen viser en STATISK forklaring uafhængig af det valgte radio-knap
  ("Henter [System] data fra et andet system via et API, så modtager [System] data") FØR valget — ordret
  samme ræsonnement som docs/csv-integrationer.md ("Et system, der henter data via et API, er modtageren").
  Dataflow-mapping (retning → fromSystemId/toSystemId) er testet med it.each i integration-form.page.spec.ts.
- Friskheds-rækkevidde (åbent spørgsmål fra plan-gennemgangen) LØST: integrationssektionen er placeret UNDER
  "Bekræft uændret"-knappen på system-detail.page.html, og hint-teksten blev ændret til at sige eksplicit
  "Integrationerne bekræftes ikke her." God løsning på det åbne spørgsmål — brug samme mønster (placering
  UNDER + eksplicit sætning) for fremtidige nye sektioner på systemsiden (kapabiliteter i delopg. 3?).
- labels.ts fik Records for IntegrationType/IntegrationRelation (det åbne punkt fra server-gennemgangen er nu
  lukket).
- 409-typer bruges korrekt i UI: "Hent nyeste version" vises KUN ved p.type === staleVersion && isEdit();
  duplicate/blocked viser bare den (allerede handlingsanvisende, server-genererede) besked.
- Modul-visning: en integration, der hænger på et MODUL af det viste system, får en synlig "(modul: X)"-note
  i tabellen — undgår "landede på en sammenfoldet liste"-fælden (linket går altid til den PRÆCISE
  modpart-systemets id, aldrig en filtreret/generel liste).
- Sletning af integration: to-trins inline-bekræftelse ("Slette integrationen permanent?" + Slet/Annullér),
  samme mønster som ved sletning af system.
- Alle knapper (Tilføj integration, Rediger pr. række, Dataobjektet findes ikke på listen) er gated på
  server-permissions (canAdd, item.integration.permissions.canEdit, me().permissions.canManageDataObjects) —
  ingen rollestrenge sammenlignes i klienten.

### Nyt fund: "heraf"-ordet i en tællelinje, der summerer på tværs af to foregående tal
system-integrations.component.ts (summary computed) skriver "Sender data til X systemer · modtager data fra Y
systemer · heraf Z lokale løsninger/udtræk". Serverens LocalSolutions er en UNION på tværs af BÅDE afsender- og
modtager-siden (IntegrationEndpoints.Summarize: sending.Concat(receiving).Distinct()), men "heraf" sidder
grammatisk lige efter kun den SIDSTE af de to forudgående tal (modtager data fra Y) — en læser vil naturligt
tro, at Z er en delmængde af Y alene. Med data, hvor den lokale løsning rent faktisk er på "sender data
til"-siden, bliver sætningen misvisende (og kan i værste fald blive tal-logisk absurd, hvis Y er lille/nul).
Selve tabellen nedenunder flagger korrekt hver række individuelt, så det er en ren tekst-tvetydighed i
tællelinjen, ikke en datafejl. VURDER VED NÆSTE ÆNDRING AF DENNE LINJE: omformulér til noget der ikke antyder
delmængde af kun ét af de to tal, fx "i alt Z lokale løsninger/udtræk (som afsender eller modtager)". Skal
rettes, men ikke blokerende for denne PR — er en ren tekstforbedring uden datamæssig konsekvens.

### Bekræftet mønster: to definitioner, der begge er korrekte hver for sig, men kan se inkonsistente ud
"Sender data til N systemer" (distinkte modparter) kan være LAVERE end antal rækker under "Sender data til" i
tabellen, hvis to integrationer går til samme system med forskellige navne (tilladt af dublet-nøglen,
beslutning C i plan.md). Vurderet OK — feltet er korrekt defineret og dokumenteret, og tabellen gør årsagen
synlig (samme systemnavn optræder to gange) — men hold øje med dette mønster, hvis der tilføjes flere
aggregerede tal på samme skærm.
