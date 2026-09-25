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

## Plan-gennemgang delopg. 3 (2026-09-24) — kapabiliteter + overlap. Tjek ved kode-gennemgangen:
- ORDKOLLISION: plan tæller "løsninger" (system+moduler = én), men typen LokalLoesning hedder "Lokal løsning/udtræk"
  og er UDELUKKET fra overlap → "Overlap: 2 løsninger" ved siden af en nedtonet "Lokal løsning" modsiger sig selv.
  Også "familie" = både system+moduler (FamilyOf/familyId) og HERM-kapabilitetsfamilie. Spørg: hvilket ord står i UI?
- Enheder på kortet: blad-badge tæller familier, grupperækker "systemCount" — skal være samme enhed og navngivet.
  "Kun uden systemer" skal bruge SAMME definition af "aktiv" som overlap (et blad med kun Nedlagt er et hul).
- Roll-up-fælde: SystemDetail.Capabilities inkl. modulers koblinger ("via modul") → formularen må KUN sende egne,
  ellers kopieres modulets koblinger til forælderen ved næste gem. Test: gem forælder uændret → ingen nye koblinger.
- SystemWriteRequest: Roles null = tøm (Apply: `request.Roles ?? []`), CapabilityIds null = uændret — modsat semantik
  i samme DTO. Kræv web-test af at formularen altid sender et array.
- Import "filen er hele modellen": beskyttelsen er REVERSIBILITET (udgået bevarer koblinger, genaktiveres ved samme
  kode) + tom fil afvist + advarsel ved stor fjernelse. Eksport må ikke indeholde udgåede (ellers genaktiverer
  eksport→import dem). Problems.StaleVersion-teksten siger "Genindlæs" — forkert handling for import (skal være
  "kør tør-kørsel igen").
- "Koblinger der skal flyttes" (udgået eller blad→ikke-blad) har ingen skærm i planen → fejler tavst. Spørg hvor.
- Ejerens "(Indfases + Udfases)" kan læses som "begge statusser udelukkes" — planen tæller Indfases med (fanger nyt
  overlap: "Konsolidering før nyt"). Den gaffel er ejerens, ikke kun "tæller Udfases?".

## Kode-gennemgang delopg. 3a server (840d4c4, PR #5, branch claude/trusting-brahmagupta-4v2ukj)
Konklusion: GOD AT LANDE. Ingen blokerende fund. Verificeret ved kørsel (ikke kun læsning): CsvReadTests (32
tests, Read(Write(x))=x for hele "Tricky"-listen inkl. apostrof-foran-apostrof), alle Capabilities-tests (65,
mod rigtig Postgres), `has-pending-model-changes` (ingen), `UPDATE_CONTRACT=1` for både CapabilityCsvTemplateTests
og OpenApiContractTests (ingen diff → openapi.json/golden-CSV er i sync), `npm run gen:api` (ingen diff →
schema.d.ts i sync).
- B6 (tom fil, advarsel ved stor sletning, det der fjernes først) og B7 (egen 409-type `stale-dry-run`,
  tekst "kør tør-kørsel igen" ikke "Genindlæs") begge LØST og testet med bånd der rammer den GAMLE værdi
  (test bruger 6→5 mod 6→4 rundt om 20%-grænsen, ikke kun ét eksempel).
- K5 (fiktive data): DevSeed's 19-punkts kapabilitetstræ er alle generiske ord ("Løn", "Bogføring",
  "Servicedesk") + "(fiktiv)" på topniveau, IKKE ægte HERM/DTU-tekst. Golden-eksempelfilen bruger "(eksempel)".
  Begge OK.
- K7 (linjenummer + Excel-vejledning): BadFiles-tabellen dækker alle ni fejltyper fra docs/csv-kapabiliteter.md
  1:1, inkl. komma-separeret fil (hint) og forkert tegnkodning (linjenummer på selve fejlen, ikke linje 1).
- Csv.Field's nye "apostrof foran apostrof": generisk i Common/Csv.cs, bruges derfor også af integrations-CSV'en
  og systemlisten — men INGEN eksisterende golden-fil indeholder et felt der starter med `'`, så ingen synlig
  ændring dér (git diff på docs/csv/integrationer-eksempel.csv var tom); kun docs/csv-integrationer.md's TEKST er
  opdateret til at nævne det. God model: en delt lav-niveau-fil ændret for én forbruger — tjek ALLE forbrugere,
  ikke kun den nye, selv når det ender uden effekt.
- Ny konflikttype stale-dry-run: Problems.cs ⇄ problem.ts begge opdateret i SAMME commit (klienten bruger den
  endnu ikke, korrekt udskudt til 3a-web — ikke en løftebrist, for der er intet UI i 3a).
- CLAUDE.md's spejl-liste opdateret korrekt (ny linje om CapabilityCsv-kontrakten, udvidet linje om Problems.cs).
- Loggen ved gennemført import (LogImported: oid, new/changed/removed) findes — "hvordan startes det med vilje"
  er policy-gated POST /api/capabilities/import (kun EA.Admin), "hvordan fejler det ikke tavst" er 409/400 til
  klienten synkront (ingen baggrundsjob at miste en fejl i).
### Mindre punkter (ikke blokerende, kan tages op siden)
- Ingen test dækker MaxRows-grænsen (>5000 rækker) eller MaxFileBytes-grænsen som EKSAKT tal i en importtest
  (kun i vejlednings-teksten via CapabilityRules-konstanten, og én "for stor fil"-test uden præcist antal
  rækker). Lavt praktisk risiko (simpelt talsammenligning), men hvis grænsen nogensinde ændres uden test,
  opdages det ikke af en mutation.
- openapi.json dokumenterer ikke 409 for /api/capabilities/import (kun 400) — men det er IKKE en regression:
  intet andet endpoint i kontrakten dokumenterer 409 heller (ProblemHttpResult er utypet for Swashbuckle
  overalt). Konsistent med resten af API'et, ikke en fælde specifik for denne PR.

## Kode-gennemgang delopg. 3a-web (3494afa, PR #6, branch claude/trusting-brahmagupta-4v2ukj)
Konklusion: GOD AT LANDE. Ingen blokerende eller BØR-fund. Verificeret ved kørsel (node24, jf. .nvmrc — node22
i shell'en giver falske lint/test-fejl, ikke et kodeproblem): `ng lint` ren, 76/76 web-tests grønne, `ng build`
OK, `npm run gen:api` uden diff (kontrakt i sync).
- Menuplacering "Systemer | Kapabiliteter" i hovedmenuen (app.ts), synlig for ALLE roller (kun authGuard på
  ruten, ikke rolle-gated) — korrekt, da visning/hent af kortet er for alle, kun import er EA.Admin. Testet
  eksplicit: "Alle kan hente kortet" (capability-map.page.spec.ts).
- Kortet er udfoldet træliste (ingen fold/collapse) — bevidst, "så browserens søgning (Ctrl+F) virker" (kommentar
  i capability-map.page.ts). God, dokumenteret begrundelse til en fremtidig "hvorfor ikke et rigtigt træ?"-spørgsmål.
- Alle tal og tekster fra planens beslutninger A/B/C/F er implementeret ORDRET: "1 ny · 1 ændret · 1 slettes ·
  17 uændrede" (importSummaryText, ental/flertal testet med bånd), Slettes-rækker først og i rødt (server-sorteret,
  klienten sorterer IKKE selv — én kilde), "(under K1)"/"(øverste niveau)" (describe()), "· beskrivelse ændret"
  KUN når navn+forælder er uændrede men beskrivelsen ikke er (den eneste situation hvor Før/Efter ellers ville se
  identiske ud på skærmen — testet eksplicit), stor-sletning-advarslen, fejltabel m. linjenummer, "Filen er
  identisk med kortet", "Gennemfør import" kun efter fejlfri tør-kørsel MED mindst én ændring OG fingeraftryk
  (canCommit), 409 stale-dry-run → "Kør tør-kørsel igen" (adskilt fra stale-version, som IKKE viser knappen —
  testet med it.each på begge typer). "Se kortet"-linket findes efter gennemført import (done-blokken).
- Server-permissions styrer knapper: canImport kommer fra CapabilityTreeResponse.CanImport (server, Policies.
  ManageCapabilities), ikke en rollestreng i klienten. Route har ikke ekstra rolle-guard — en læser der navigerer
  direkte til /kapabiliteter/import ser "Kun enterprise arkitekten kan importere kortet." (testet), og POST'en er
  selvstændigt policy-gated server-side (bekræftet i 3a-server-gennemgangen) — korrekt forsvar i dybden.
- G (dækning "X af Y systemer") og H (udgåede kapabiliteter med koblinger, egen sektion) er IKKE i denne skive —
  korrekt, de hører til 3c hhv. 3b pr. planens skæring, og ingen tekst i 3a-web foregiver at de findes.
- To KAN-fund (ikke blokerende, ingen handling krævet nu):
  1. labels.ts: JSDoc-kommentaren "/** "Forælder › Modul" for et modul, ellers bare navnet. */" er nu FYSISK
     adskilt fra sin funktion (systemDisplayName) af den nyindsatte importSummaryText, som fik sin egen kommentar
     lige under — ren cut/paste-forskydning, ingen funktionel effekt, men vildledende ved næste læsning oppefra.
  2. capability-import.page.ts: fejler GET /api/capabilities (canImport-opslaget) på selve importsiden, vises
     kun en generisk problem-besked (toProblem uden præfiks) — filvælgeren forbliver usynlig uden forklaring på
     HVORFOR (canImport() er null, hverken true eller false-grenen rammer). Lavt praktisk risiko (samme kald
     lykkes på kortsiden lige før), ingen test dækker det. Tjek ved næste ændring af denne side.
- Mønster at genbruge: "beskrivelse ændret"-annotationen er et godt mønster for "diff af et felt, der ikke selv
  vises i tabellen" — spørg om samme mønster er nødvendigt, når en fremtidig ændringstabel har et felt, der ikke
  indgår i den korte visningstekst (fx en fremtidig CSV-import med et lignende "usynligt" felt).

## Kode-gennemgang delopg. 3b server (89d8011, PR #7, branch claude/trusting-brahmagupta-4v2ukj)
Konklusion: LAND MED FORBEHOLD. Én BLOKERENDE talfejl, to BØR. Verificeret ved kørsel: 109/109 API-tests i
Capabilities-mappen grønne, has-pending-model-changes ren, openapi.json/schema.d.ts-diff matcher DTO 1:1, live
dry-run-kald mod kørende dev-API (kun GET/tør-kørsel, intet gennemført/rettet i ea_dev).
- BLOKERENDE, NYT MØNSTER: "to tal-definitioner af samme begreb i samme summary-DTO, hvor kun ÉN af dem
  (booleanen) bruger den korrekte delmængde." CapabilityImportSummary.LargeRemoval bruger korrekt
  `removedFromMap` (kun det, der faktisk var synligt på kortet FØR importen), men UI-teksten
  ("Importen fjerner N af kortets M kapabiliteter", capability-import.page.html) bruger `removed + retired`,
  som OGSÅ tæller en allerede-udgået-og-nu-koblingsløs kapabilitets sletning ("usynlig oprydning" — den var ikke
  på kortet i forvejen). Bevist ved en midlertidig, ikke-committet test: 3 aktive + 1 allerede udgået (koblet);
  fjern 1 fersk aktiv + ryd den udgåedes sidste kobling i samme omgang → Removed=2, men kun 1 forsvinder reelt
  fra det synlige kort — teksten ville sige "fjerner 2 af 3" (100 % for højt). SPØRG FREMOVER, når en advarsel
  og en boolean deles om samme sum: bruger de PRÆCIS samme underliggende tælling, eller har den ene en
  "usynlig" komponent den anden ikke har? Ret: eksponér removedFromMap som sit eget summary-felt, brug det
  begge steder.
- BØR: "FaarUnderkapabiliteter" (blad→ikke-blad) har — modsat "Udgaar" — INGEN permanent hjemsted efter importen.
  Udgåede kapabiliteter fik en rigtig løsning i denne PR (RetiredCapability i GET /api/capabilities, med
  Systems+RetiredPath — B3 fra plan-gennemgangen er nu LUKKET). Men et blad, der får børn og stadig har egne
  koblinger, er kun synligt i DEN ENE tør-kørsel, der udløste det (CapabilityChangeKind.FaarUnderkapabiliteter,
  kun i det import-svar) — hverken CapabilityNode (kortet) eller SystemCapabilityDto (systemsiden) har et flag
  for "denne kobling sidder nu på en ikke-blad". Går admin videre uden at handle med det samme, findes
  koblingen ikke igen nogen steder. Samme "fejler tavst"-mønster som B6 advarede om, nu kun halvt løst
  (udgået-grenen løst, blad→ikke-blad-grenen ikke). Tjek ved 3b-web/3c, om dette bliver løst der, eller om det
  er en bevidst accepteret restrisiko, der bør stå i planen.
- BØR (opfølgning, ikke blokerende): Retired-sektionen PÅ SELVE KORTET (beslutning H, "får deres egen sektion på
  kortet") er endnu ikke bygget i web (capability-map.page.html refererer slet ikke `retired`-feltet), og
  docs/plan.md's skæringsliste nævner kun "3b-web: systemside, formular og filter" — IKKE kortet selv. Risiko
  for at falde mellem to stole i skæringen. Spørg Arkitekten/Release Manager, hvilken skive der bygger den.
- Bekræftet GODT: familie-reglen for capabilityId=none (forælder dækket af moduler, modul af forælder, IKKE
  søskende) implementeret præcis som planlagt og testet inkl. søskende-modeksempel — en god skabelon for
  "familie, men ikke søskende"-regler fremover. FK Restrict på SystemCapability.CapabilityId er forsvar i
  dybden ud over applikationslogikken (importen kan aldrig fysisk slette en kapabilitet med koblinger, selv hvis
  Plan()-logikken skulle fejle). CapabilityRules.Ordered() filtrerer RetiredAt ét sted, genbrugt af BÅDE
  GetTree og Export — ingen dobbelt-vagt, god model.
- Uden for mit mandat, men vigtigt at nævne: en samtidig Security Reviewer-kørsel havde ukommitterede rettelser
  i samme filer (SystemEndpoints.cs m.fl.) for et bekræftet TOCTOU-fund (ValidateCapabilities uden lås, race
  mod import → 500 i stedet for 409). Skal committes og lande SAMMEN med denne PR. Tjek status ved næste
  gennemgang af samme gren.

## Kode-gennemgang delopg. 3b-web (7e785e2, branch claude/trusting-brahmagupta-4v2ukj) — kun web
Konklusion: GOD AT LANDE. Ingen blokerende eller BØR-fund. Verificeret ved kørsel: `ng lint` ren, 96/96
web-tests grønne (op fra 76+), `ng build` OK, ingen `git status`-diff efter build (ingen kontrakt-drift, som
forventet — ingen server-DTO ændret i denne skive). Visuelt efterprøvet med Playwright/Chromium mod kørende
dev-API+web (Eva Arkitekt): kort (kun blade linker, grupper er ren tekst), filtreret systemliste (navneopslag
virker), systemside (egne + families kapabiliteter, "via forælderen/modulet"), redigeringsformular (chips for
alle tre egne koblinger).
- LUKKER et tidligere BØR-fund (3b-server-gennemgangen, 89d8011): "FaarUnderkapabiliteter har intet permanent
  hjemsted efter import" er nu løst — SystemCapabilityDto.capability.moveReason (permanent, på systemsiden) og
  CapabilityTreeResponse.toMove (permanent, del af det almindelige GET /api/capabilities-svar, ikke kun ét
  dry-run-svar) dækker begge grene (Udgaaet og HarUnderkapabiliteter) samme sted. God løsning — ingen kobling
  kan længere "forsvinde" efter det ene dry-run, der udløste den.
- Server-diff-semantikken er RIGTIGT forstået af klienten: SystemEndpoints.ValidateCapabilities valmakere kun
  NYE id'er (ikke allerede-koblede) mod selectable-reglen; klienten sender ALTID hele ownCapabilities-listen
  tilbage (inkl. udgåede/flaggede), server beholder eksisterende koblinger uanset status. Testet begge veje
  (web: "sender HELE listen … men aldrig familiens"; server allerede testet i 3b). Flytning sker ved at fjerne
  chippen (altid muligt, matChipRemove uden selectable-tjek) + tilføje en ny via autocomplete (kun selectable).
- capabilityIds sendes ALTID som array (aldrig null) fra formularen — også ved oprettelse af nyt system (tomt
  array) og ved uændret gem (fuld liste) — undgår null-betyder-uændret-fælden fra planen. Testet eksplicit med
  kommentar om hvorfor ("En tom liste, ikke null").
- Listefilterets navneopslag (loadCapabilityName) slår op i BÅDE tree.items og tree.toMove, så et link til en
  UDGÅET kapabilitet (som ikke findes i det aktive træ) stadig får et navn, og filtreringen selv sker uafhængigt
  af opslaget (capabilityId sendes til systemer-endpointet uanset om navnet blev fundet). Fejler opslaget (500),
  falder UI'et tavst tilbage til "Valgt kapabilitet" uden fejlbanner — bevidst og testet ("kan navnet ikke
  hentes, filtreres der alligevel"); vurderet OK, fordi selve filtreringen (det der betyder noget) ikke fejler,
  kun en sekundær visningstekst. Server-filteret bekræftet at matche PRÆCIS (ingen undertræ-filtrering, heller
  ikke for retired-id'er): `CapabilityLinks.Any(l.CapabilityId == capability)`, uden RetiredAt-betingelse.
- Friskheds-rækkevidden (mønster fra tidligere gennemgange) udvidet korrekt: hint-teksten under "Bekræft
  uændret" blev ændret fra "Systemets oplysninger …" til "Systemets oplysninger og EGNE kapabiliteter …" —
  ordet "egne" er præcist (udelukker familiens skrivebeskyttede koblinger, som vises men ikke ejes af dette
  system). /confirm-endepunktet selv er uændret (rører ikke capabilityIds), så "Bekræft uændret" ændrer stadig
  ikke data — kun teksten ved siden af blev udvidet til at nævne det nye synlige felt. God skabelon at genbruge.
- "Kun blade linker" (design-valget fra opgavebeskrivelsen) holder både på kortet (n.selectable, som er
  leaf+ikke-udgået) og er IKKE brugt samme sted på systemsidens egen kapabilitetsliste (dér linker ALLE koblinger,
  inkl. udgåede/ikke-blade, til den eksakt-filtrerede liste) — det er bevidst OG korrekt, fordi filteret altid er
  eksakt uanset kilde; kun kortets grupper (som reelt ikke har direkte koblinger) er udelukket fra at linke.
- moveReasonLabels (lang form, kort/systemsiden) og moveReasonShortLabels (kort form, chip på formularen) er to
  Records for samme enum — ikke en dobbelt-vagt (begge er ren tekst, ingen forretningslogik), men et mønster at
  genkende, hvis en fremtidig gennemgang undrer sig over to labels-opslag for samme enum.

## Plan-gennemgang 3c/3d (2026-09-24, Arkitektens plan A→E, main 391e82b). Tjek ved kode-gennemgangen:
- ENHEDER PÅ KORTET: dækning "X af Y systemer" tæller RÆKKER (moduler for sig, nedlagte med), overlap "N systemer"
  tæller FAMILIER (FamilyKey inkl. søskende). Samme ord, to enheder på samme skærm → krævede "systemer og moduler".
- Badge vs. listen bag linket: bladets link viser HOLDERE (alle statusser, modul og forælder som to rækker), badgen
  tæller familier der tæller. Krævede medlemsnavne (OverlapMember) i Kun overlap-visningen.
- O4 "Planned=0 når Counted=0" giver en CSV-række med TællerIkkeMed=Planlagt og AntalPlanlagte=0 → selvmodsigelse.
  Forslag: rå tal + server-boolean for mærket. Og "Kun overlap"-filteret skjuler "planlagt oven på aktivt".
- 3d "filen er det fulde sæt for systemerne i filen": at slette et systems SIDSTE række = "spring over", ikke
  "fjern" → usynligt. Forslag: vejledning "tøm Kode", og tør-kørslen NAVNGIVER systemer, der ikke står i filen.
- Gammel eksport genindlæst: UpdatedAt er IKKE rørt af /confirm (kun LastConfirmed*) → en `SidstÆndret`-kolonne
  kan advare uden de falske alarmer, Arkitekten frygtede ved xmin/Version. Genbrug-idé til fremtidige imports.
- Beregnede kolonner i en import-CSV (Status, Forretningsejer…) ser redigerbare ud i Excel og ignoreres tavst →
  spørg altid: opdager tør-kørslen, at en ignoreret kolonne er ændret?
- system-integrations.component.ts skriver stadig "lokale løsninger/udtræk" (flertal) på systemsiden → en
  "løsninger står ikke på skærmen"-test på systemsiden er kun grøn, hvis fixturet mangler ≥2 lokale løsninger.
- "Familie" i UI/docs = HERM's øverste niveau (csv-kapabiliteter.md). Brug aldrig ordet om system+moduler på skærmen.

## Kode-gennemgang delopg. 3d-server (d3c91b3 + b8d6ff5, PR #10, branch claude/trusting-brahmagupta-4v2ukj)
Konklusion: GOD AT LANDE. Ingen blokerende eller BØR-fund. Verificeret ved kørsel (171/171 Capabilities-tests,
inkl. 53 Coupling-specifikke) OG ved levende dry-run mod kørende dev-API med fem konstruerede scenarier (kun
GET/tør-kørsel, intet gennemført i ea_dev): (A) én kobling-række slettet af flere for samme system → kun DEN
kobling fjernes; (B) ALLE et systems rækker slettet → systemet optræder korrekt i `notInFile`, røres slet ikke
(bekræfter beslutning M/plan-fund 6: "slet sidste række" er nu synlig, ikke tavs); (C) `FuldtNavn` ændret
(simuleret omdøbning) → blokerende fejl med præcis besked; (D) delvis fil (kun `Overlap=Ja`-rækker, 2 af 10
systemer) → de 8 udenfor korrekt listet i `notInFile`, ingenting slettet for dem; (E) en kolonne fjernet i Excel
→ afvist med præcis "Første linje skal være …"-besked. Alle fem af mine egne plan-fund (3,4,5,6,11) er ført ind
1:1 i beslutning M i docs/plan.md OG i koden:
- `SidstÆndret` (fuld præcision, `T`/`Z` forhindrer Excel i at gøre det til en dato) sammenlignes mod
  `system.UpdatedAt` og giver `ChangedSinceExport` PR kobling, ikke kun pr. system — testet med bånd (Kompas
  redigeret → true, Ugle kun bekræftet → false, samme scenarie, viser at kun redigering, ikke bekræftelse,
  udløser advarslen).
- "Tøm koden, slet ikke rækken": dokumenteret præcist i docs/csv-koblinger.md ("Indlæs filen igen"-afsnittet,
  nyt i denne PR) OG håndhævet i kode via `notInFile` (systemer med koblinger, der ikke har nogen række i filen)
  — begge veje verificeret live (scenarie B).
- 20 %-advarslen (`LargeRemoval`) bruger korrekt `removed+unchanged` (koblinger FØR importen på systemerne i
  filen) som nævner, ikke et tal der inkluderer `added` — samme mønster som 3a's B6-fund. Testet med bånd (1/5 =
  20 % → false, 2/5 = 40 % → true), kommentar nævner eksplicit at `>=` ville give forkert resultat ved 20 %.
- Advarsler om ignorerede kolonner (`IgnoredEdits`/`Warnings`) dækker kun `CouplingCsv.SystemColumns` (de
  system-relaterede felter en forvalter kunne tro var redigerbare: Forælder, Status, Type, ForvaltendeTeam,
  Forretningsejer, Systemejer, SidstBekræftet, Systembeskrivelse) — IKKE de rene overlap-beregnede kolonner
  (Kapabilitet, Sti, Overlap, SystemerDerTæller, AntalPlanlagte, TællerIkkeMed, DelesMed, BørFlyttes). Bevidst og
  fornuftigt: kun `SystemId`+`Kode` er nøglerne, og de udeladte kolonner ligner ikke redigerbare felter på samme
  måde. Ingen risiko fundet ved gennemgangen.
- `FuldtNavn`-udvejen (tøm cellen for at omgå omdøbnings-tjekket) er dokumenteret og testet begge veje (udfyldt
  og forkert → fejl; tom → ingen tjek).
- Fem sandsynlige fejlscenarier fra opgavebeskrivelsen (slettet række, gammel eksport, delvis fil, omdøbt system,
  kolonne slettet i Excel) er ALLE enten direkte testet i CouplingImportTests.cs eller verificeret live i denne
  gennemgang (se ovenfor). "Datoer/tal ændret af Excel" er dækket strukturelt: kun `SystemColumns` (streng-
  sammenligning, ingen parsing) advarer, og de rene talkolonner (AntalPlanlagte mv.) indlæses slet ikke, så en
  Excel-ombygget talformatering kan ikke korrumpere noget. "Modul dækket af forælderen" er ren dokumentation
  (findes via "Hent systemliste (CSV)"), ikke en kode-sti der kan fejle — ingen særbehandling nødvendig, da
  ethvert gyldigt SystemId (modul eller ej) virker ens i `Plan()`.
- Delte flader alle bekræftet uændrede/korrekt udvidet: kortimportens 409-tekst er BOGSTAVELIGT uændret (samme
  streng, nu produceret via `Problems.StaleDryRun(subject)` med `subject="Kortet eller koblingerne til det"` —
  verificeret ved diff, ikke kun læsning). `DeleteSystem` tager nu koblingslåsen FØR systemrækken, i samme
  rækkefølge som importen — testet med en ægte race (rå SQL-forbindelse holder låsen, sletning venter uden selv
  at have låst systemet, `FOR UPDATE NOWAIT` beviser det). `Common/CsvImport.cs` er en ren generalisering
  (commit d3c91b3, "uden ny adfærd") af det, der allerede lå i `CapabilityImport.cs` — ingen ny fejlvej fundet.
  64 MB-grænsen har en KODET begrundelse (`En_eksport_af_hele_DTU_kan_indlaeses_igen`-testen, 2000 systemer × 5
  koblinger, dobbelt margin), ikke en påstået konstant.
- `Version`/xmin: `Apply()` sætter kun `UpdatedAt` på systemer, der FAKTISK ændres (`Diff.Where(add+remove>0)`),
  ikke på alle systemer i filen — testet eksplicit (uændret re-import: samme `UpdatedAt`/`Version` før/efter).
  Undgår falske "ændret af en anden"-konflikter på formularer, der er åbne på urørte systemer i samme fil.
- Ny enum `CouplingChangeKind` (Fjernes/Tilfoejes) har INGEN post i `labels.ts` endnu — korrekt udskudt, ingen
  web-kode refererer den, og header-genkendelsen ("det ligner en koblingsfil", knappen "Importér koblinger") er
  bevidst udskudt til 3d-web sammen med selve UI'et. `CouplingImportResult`/`CouplingChange`/`CouplingImportSummary`
  giver, hvad en 3d-web-side skal bruge (linje+kolonne på fejl/advarsler, before/after via Kind, fingeraftryk,
  `notInFile`-liste) — samme facon som 3a-web's velprøvede mønster (fejltabel, "Filen er identisk", canCommit).
  Vurderet: 3d-web bør kunne bygges UDEN yderligere serverændringer.

## Kode-gennemgang delopg. 3d-web (eb6a1df + 4a554fd + 860d661, PR #11, branch claude/trusting-brahmagupta-4v2ukj)
Konklusion: GOD AT LANDE. Ingen blokerende eller BØR-fund. Verificeret ved kørsel (node24 — node22 giver falske
fejl, se tidligere note): `ng lint` ren, 106/106 web-tests grønne (op fra 96), `ng build` OK (coupling-import-page
er sin egen lazy chunk, samme mønster som capability-import-page). Verificeret LIVE mod kørende dev-API+web (Eva
Arkitekt, kun GET/tør-kørsel, intet gennemført i ea_dev):
- HEADER-KRYDSGENKENDELSEN virker end-to-end gennem den RIGTIGE UI, ikke kun unit-test: downloadede koblings-CSV'en
  via UI'et og uploadede den uændret til kort-importsiden → præcis fejlteksten "Det er filen med koblinger, ikke
  kortet. Den indlæses med \"Importér koblinger\" på siden Kapabiliteter." (linje 1, ingen kolonne). Teksten
  matcher bogstaveligt knappens tekst på den anden side (case-følsomt, testet begge veje server-side med
  SequenceEqual på hele header-listen — kun eksakt match udløser genkendelsen, en delvis afvigelse falder tilbage
  til den generiske "Første linje skal være …"-fejl, hvilket er en fornuftig grænse for mekanismen).
- "Tøm koden, slet ikke rækken" vs. "slet hele systemets sidste række" ER FAKTISK TO FORSKELLIGE STIER, verificeret
  live med to konstruerede filer mod ægte dev-seed-data: (1) fjernede ALLE linjer for ét system (Laborant) → korrekt
  i `notInFile`, systemets kobling rørt IKKE; (2) tømte kun `Kode`-cellen på én linje for et system, der stadig har
  andre linjer i filen → korrekt registreret som "Fjernes" for netop den kobling. Begge scenarier var i mine egne
  plan-fund (delvis fil, slettet række) og er nu bekræftet reelt, ikke kun i CSV-testfixtures.
- `couplingImportSummaryText` matcher forslaget fra plan-gennemgangen ("12 koblinger tilføjes · 3 fjernes · 240
  uændrede — på 15 af filens 280 systemer" → landede som "… — 3 af filens 14 systemer ændres", udelod kun ordet
  "på" og flyttede "ændres" til slutningen — samme information, læses naturligt, ingen indholdsmæssig afvigelse).
- Alle fire knaptekster ("Hent kortet (CSV)", "Importér kort", "Hent koblinger (CSV)", "Importér koblinger") er
  grep-bekræftet IDENTISKE på tværs af knap, serverens fejltekster (begge retninger), docs/csv-koblinger.md og
  CLAUDE.md's spejl-liste — ingen af de fire strenge afviger noget sted.
- Ingen "løsninger"/"familie" på siden: dækket af eksplicit negativ regex-test (`not.toMatch(/løsninger|famili/i)`
  på HELE sidens tekst, ikke kun en enkelt linje) — det korrekte mønster (genkend på POSITIV FRAVÆR-test, ikke kun
  fravær af assertion).
- `ImportFlow<T>` (ny fælles klasse i core/import-flow.ts) er en ren udtrækning af den tilstandsmaskine, kort-
  importsiden allerede havde (fil → tør-kørsel → commit m. fingeraftryk) — CapabilityImportPage er omskrevet til
  at BRUGE den (ikke duplikeret), verificeret ved diff at dens template/tests er uændrede i adfærd (samme
  data-testid'er, samme rækkefølge). God model: fælles tilstandsmaskine, sider beholder hver deres tekster/DTO.
- Kortets header ændret fra to enkeltstående knapper til to `role="group"`-blokke (Kortet / Koblinger), testet
  eksplicit at begge canImport-styrede knapper (import + import-couplings) er server-permission-gatede hver for
  sig, og at koblings-gruppen kun vises når `t.items.length` (intet map = ingen mening i at hente/importere
  koblinger endnu) — bevidst og korrekt, ikke en overset sammenhæng.
- Route-placering: `/kapabiliteter/koblinger/import`, nested under samme Kapabiliteter-forælder som
  `/kapabiliteter/import` — konsistent placering, ingen ny topmenu-indgang (koblinger er ikke et selvstændigt
  koncept for en bruger, kun en gren af kapabilitetskortet).

## Kode-gennemgang delopg. 3c-2 (f18dd09, PR #12, branch claude/trusting-brahmagupta-4v2ukj) — kun API
Konklusion: GOD AT LANDE. Ingen blokerende fund. Verificeret ved kørsel (313/313 API-tests, `dotnet format`
ren, `has-pending-model-changes` ren, `UPDATE_CONTRACT=1` OpenApiContractTests uden diff, `npm run gen:api`
uden diff, web: `ng lint` ren, 106/106 (uændret antal — korrekt, ren API-skive uden nyt web), `ng build` OK —
alt kørt på node24, jf. tidligere note om at node22 giver falske fejl) OG ved LIVE kald mod kørende dev-API
(Eva Arkitekt, kun GET, ingen skrivning): overlap for søskendemoduler (K3.1.2: Nordlys Økonomi + Nordlys
Projekter, begge moduler af Nordlys ERP, tæller Counted=1, ikke 2), family-udelukkelse i SharedWith (Nordlys
Projekters side viser tomt SharedWith, fordi eneste anden holder er søskendemodulet), OwnExclusion for en
udfaset holder, og PlannedOnTopOfActive uden selvmodsigelse ved Counted=0 (Studieadministration/Optagelse:
counted=0, planned=1, plannedOnTopOfActive=FALSE — modsat Kompas Sag/Studium: counted=1, planned=1, true).
- FUND (bekræftet reel drift, nu rettet — ikke kun en stilrettelse): FØR denne PR holdt Beslutning K's løfte
  ("Det er samme regel som 'Ikke angivet' på systemlisten … findes ét sted i koden") IKKE i praksis.
  `ExportCouplings` filtrerede allerede `Uncovered && LifecycleStatus != Nedlagt` (to Where-led), men
  systemlistens `capabilityId=none`-filter brugte kun rå `Uncovered` UDEN nedlagt-udelukkelsen (git-verificeret
  ved `git show 6e2cb6d`). Et nedlagt, ukoblet system stod altså IKKE på CSV'ens manglende-liste, men VILLE stå
  under "Ikke angivet" på systemlisten — to forskellige svar på "mangler dette system en kobling?". Denne PR
  retter det ved at trække begge Where-led ud i `CapabilityQueries.Missing()` og bruge den begge steder (samt i
  det nye `CouplingCoverage`-tal). Testet eksplicit (`Daekningen_er_praecis_systemlistens_Ikke_angivet_...`,
  System E+F nedlagt+ukoblet, IKKE på listen, IKKE talt med). LÆR: når en beslutning i plan.md siger "findes ét
  sted i koden", så VERIFICÉR det (grep efter alle forbrugere af den underliggende regel), stol ikke på at
  teksten allerede var sand bare fordi den stod i en tidligere godkendt plan — den kan sagtens beskrive en
  FREMTIDIG tilstand, planen selv ikke havde opdaget var brudt endnu.
- Genbrug, ikke duplikering: `OverlapHolder`/`OverlapAssessment`/`CapabilityRules.OverlapOf` fandtes allerede
  (3c-1, til CSV'en) — denne PR eksponerer PRÆCIS samme funktion via API'et (kort + systemside), ingen ny
  parallel beregning. God model, matcher plan-kravet "overlap … den ENESTE funktion".
- Ydelse: `GetTree` henter alle overlap-holdere i ÉT lookup-kald for hele træet (ingen N+1 pr. knude).
  Systemsidens `CapabilitiesOf` tilføjer ÉN ny forespørgsel pr. visning (`OverlapHoldersAsync` scoped til kun
  systemets egne+families kapabilitets-id'er), og KUN når systemet har mindst én kobling (tidligt return ellers)
  — acceptabelt, ikke en N+1-fælde.
- API-formen er nok til 3c-web UDEN flere serverændringer: dækningslinjen (`CouplingCoverage`), "Overlap og
  planlagte"-filter+tællelinje (klienten kan tælle på det allerede hentede træs `Overlap.IsOverlap`/`.Planned`),
  mærke+medlemmer (`Overlap.Members`), "Deles med"/"tæller ikke med" (`SharedWith`/`OwnExclusion`), og
  hjælpelinjen på en kapabilitetsfiltreret systemliste (samme mønster som 3b-web's `loadCapabilityName`: slå op
  i det allerede hentede træ, som nu OGSÅ har Overlap pr. knude — ingen ny visning kræver et nyt endpoint).
- MINDRE (ikke blokerende): `CouplingCsv.cs`'s XML-doc-kommentar refererer stadig `<see cref="CapabilityQueries.
  Uncovered"/>`, som nu er `private` (utilgængelig cross-ref fra en anden klasse) — ingen build-advarsel udløst,
  men lidt vildledende ved næste læsning. Overvej at pege den på `Missing` i stedet, næste gang filen røres.
- IKKE en fejl, men værd at holde øje med: `status=Nedlagt` OG `capabilityId=none` sammen giver ALTID et tomt
  resultat (Missing() udelukker nedlagte pr. definition) — logisk konsekvent med den nye label "Ikke angivet
  (nedlagte undtaget)", ingen modsigelse, men listen har ingen forklarende tomme-tilstand for netop DENNE
  filterkombination. Lav risiko, ingen handling krævet nu.

## Kode-gennemgang delopg. 3c-web (d2a134a, PR #13, branch claude/trusting-brahmagupta-4v2ukj) — kun web
Konklusion: GOD AT LANDE. Ingen blokerende fund. Verificeret ved kørsel (node24 via /opt/nvm — installeret i
denne session, se note nedenfor): `ng lint` ren, 114/114 web-tests grønne (op fra 106), `ng build` OK,
`npm run gen:api` mod kørende dev-API (:5080) uden diff (kontrakt i sync, ingen ny DTO i denne skive — 3c-2's
DTO'er genbruges råt).
- Alle plan-punkter E/F/I/J/K/N er ført ind, ikke kun i tekst men i data-flow: dæknings-linjen
  (`t.coverage.covered/total`) og "se de manglende"-linket (`/systemer?capabilityId=none`) bruger BEGGE
  serverens `CapabilityQueries.Missing()` uændret fra 3c-2 (ingen ny klient-beregning). Badge og medlemsliste på
  kortet læses fra SAMME `n.overlap`-objekt (badges()/memberText() er rene formatter-funktioner, ingen egen
  tælling) — kan pr. konstruktion ikke modsige hinanden.
- LUKKER to tidligere noterede fund fra denne fil: (1) "familie" om system+moduler → omdøbt til "Kapabiliteter
  via forælder eller moduler" (system-form.page.html) og "Et system og dets moduler tæller som ét system"
  (system-detail, capability-map); (2) "lokale løsninger/udtræk" (flertal) i system-integrations.component.ts →
  "af typen Lokal løsning/udtræk" (ental, enum-navnet), MED en positiv OG en negativ test
  (`not.toContain('løsninger')` og en tilsvarende regex-test på hele kortsiden `not.toMatch(/løsninger|famili/i)`
  efter toggle) — godt mønster, genkender på fravær af det forbudte ord over HELE sidens tekst, ikke kun én linje.
- Own-exclusion-teksten skelner korrekt mellem egen kobling og familiens: `c.heldBy` er null for systemets EGEN
  kobling → "Dette system tæller ikke med i overlap (…)"; for en kobling arvet fra forælder/modul → "{modulnavn}
  tæller ikke med i overlap (…)". Testet eksplicit begge grene i samme test (system-detail.page.spec.ts) — det
  var netop den fælde, opgaven bad om at tjekke.
- "Deles med" og own-exclusion er bekræftet NEUTRAL, ikke en fejl: `.shared`/`.muted`-klasse (ingen
  error-container-farve som `.flag`/moveReason bruger), `role="alert"` bekræftet FRAVÆRENDE i testen. God
  adskillelse af "informativ" vs. "kræver handling"-styling på samme skærm.
- Toggle "Overlap og planlagte" bruger `router.navigate(..., replaceUrl: true)` — SAMME etablerede mønster som
  system-list.page.ts's søge-/dropdown-filtre (allerede godkendt tidligere, ikke nyt). Gennemgået: browser-tilbage
  virker korrekt, fordi replaceUrl ændrer URL'en på den SIDDENDE historik-post (så et link/tilbage til den post
  stadig bærer `?overlap=1`); komponenten gendannes fra `route.snapshot` ved ny instansiering (væk fra og tilbage
  til siden), ikke fra en løbende subscription — testet eksplicit at `?overlap=1` i URL'en giver korrekt
  starttilstand ved direkte navigation. INGEN ny historik-post pr. toggle-klik (bevidst, matcher eksisterende
  filter-mønster; forskellige toggle-tilstande er ikke hver sin "tilbage"-destination) — konsistent, ikke en fejl.
- IKKE-BLOKERENDE (test-hul, værd at nævne for Test Manager): `[attr.aria-pressed]="overlapOnly()"` findes i
  HTML'en (functionelt korrekt — Angular sætter "true"/"false"-strengen på selve DOM-attributten for boolean attr-
  bindings, fjerner den ikke ved false), men INGEN test læser selve attributværdien (kun knappens synlige tekst
  testes). En mutation, der fjerner bindingen eller bytter den om, ville ikke blive fanget af nuværende suite.
  Foreslå: en test der læser `button.getAttribute('aria-pressed')` før/efter klik.
- MINDRE (ikke blokerende, ren tekstnuance): attention-linjen "N kapabilitet(er) med overlap · M med et planlagt
  system oven på et aktivt" udelader ordet "kapabilitet(er)" i andet led (kun tallet M) — grammatisk gyldigt (der
  refereres til "kapabiliteter" implicit fra sætningens opbygning), men lidt tættere formuleret end første led.
  Ikke en fejl, bare en mulig fremtidig klarhedsforbedring hvis nogen render tvivl.
- Node-miljø: dette sandbox havde hverken node24 (kun node22 forudinstalleret) — løst med `nvm install 24` via
  `/opt/nvm` (NVM_DIR=/opt/nvm, IKKE $HOME/.nvm — bash -lc og efterfølgende separate Bash-kald har forskellige
  shell-init, så `source ~/.nvm/nvm.sh` fejlede i et senere kald selvom installationen lykkedes). Genbrug denne
  opskrift (`export NVM_DIR=/opt/nvm; . "$NVM_DIR/nvm.sh"; nvm use 24`) i stedet for at gengive node22-fejlen som
  et kodeproblem.

## Plan-gennemgang delopg. 4 + Firebase-drift (2026-09-25, main a517cc0). Tjek ved kode-gennemgangen:
- INVITATION HAR INGEN UI: personformularen (system-form.page.html, "Personen findes ikke på listen?") har kun Navn +
  Afdeling, INGEN e-mail — og login binder på e-mail. Person kan ikke redigeres. Spørg altid: "hvordan giver EA en
  kollega adgang, klik for klik?" og "hvor ser EA, hvem der har adgang / har logget ind?"
- Sletteret ≠ redigeringsret: DeleteSystem bruger i dag Policies.EditSystem. Når forvaltere får EditSystem, skal
  sletning have egen admin-policy FØR låsen (403), ikke en tekst i DeleteBlockedReason (ren brugs-funktion → 409).
- "Sidst ændret" (kun systemrækken) står lige ved historikken, hvor integrationsændringer er nyere → modsigelse.
  Historik tom på eksisterende systemer mens "Sidst ændret" viser en dato → skriv "føres fra <dato>".
- Forældreskift i historik skal stå på gammel OG ny forælder; slettet modul på forælderen; person-oprettelse
  logges men har ingen visning (lover mere end den giver).
- toProblem() fladgør ALLE 403 til "Du har ikke adgang…" og taber type → en ny 403-type (not-registered) forsvinder.
  authGuard/getToken() er synkrone og kalder logout() i catch → Firebase-sessionen skal awaites (authStateReady).
- Banner "Prototype — alle data er fiktive" (app.ts) bliver usandt med rigtige login-brugere.
- Import-grænser vs. Cloud Run: koblingsimport 64 MB (CouplingImport.MaxFileBytes), Cloud Run 32 MiB, Hosting 60 s.
- E-mail-levering til @dtu.dk (spam/Safe Links, afsender firebaseapp.com ligner phishing) er den største praktiske
  login-risiko — ikke dækket af E2E (emulator skåret væk).

## Kode-gennemgang delopg. 4a (9db34ad, PR #14, branch claude/trusting-brahmagupta-4v2ukj) — server + minimale form-tekster
Konklusion: GOD AT LANDE. Ingen blokerende fund. Verificeret ved kørsel (325/325 API-tests, `dotnet format`
ren, `has-pending-model-changes` ren, web: `ng lint` ren, 116/116 web-tests grønne) OG ved LIVE dev-login (Eva +
Frida, ægte kørende API+web, kun GET/enkelte skriv-/slet-kald på Frida uden retten hertil):
- Alle fem plan-fund fra "Plan-gennemgang delopg. 4" (2026-09-25) er ført ind: B2 (sletning har EGEN
  `Policies.DeleteSystem`, tjekket FØR koblingslåsen, 403 ikke 409 — testet live: `DELETE` som Frida på et system
  hun kan REDIGERE men ikke slette giver 403, teksten er ordret beslutning R'ens tekst) — mit tidligere fund er
  altså lukket, ikke kun i planen.
- O (redigeringsret) og Q (forælderskift) er begge implementeret ÉT sted (`SystemAccess.CanEditAsync`,
  `MoveSystemHandler`) og BEVIST ved mutationstest i denne gennemgang (ikke kun læst): fjernede modul-arv
  (`direct.Concat(modules)` → kun `direct`) i `SystemAccess.cs`, kørte `StewardAccessTests` → 3 af 11 tests røde;
  gendannet bagefter (kun mod committet kode, ingen ukommitteret ændring tabt). Testsuiten (`StewardAccessTests.cs`,
  262 linjer, ny fil) dækker PRÆCIS opgavens tjekliste 1:1 med både positiv og negativ case i samme test: egne
  systemer+moduler ja/andres nej, rolle på modul giver IKKE ret over forælder, modul flyttes KUN til forælder man
  selv kan redigere (inkl. "ud af en forælder man ikke kan redigere" = Forbidden), sletning kun EA, integrationer
  "en af enderne men ikke kun via platformen" (inkl. kandidatlisten/CanAdd), og "fjerner sig selv → mister CanEdit
  i SAMME svar" (P). Live-bekræftet på ægte DevSeed-data (Frida er Systemforvalter på Nordlys ERP og Laborant):
  `GET /api/systems/{Nordlys HR}/…` → canEdit=true (arvet fra forælder), `parent-candidates` for Nordlys HR viser
  KUN Laborant+Nordlys ERP (hendes to redigérbare systemer), ikke resten af registret.
- Disabled reactive-form-felt-fælden (kunne have været en STILLE datafejl): et låst `parentSystemId`
  (`form.controls.parentSystemId.disable()`) risikerer at Angular UDELADER feltet fra `.value` ved gem — komponenten
  bruger korrekt `getRawValue()`, og der ER en dedikeret ny web-test for netop dette ("et låst forælder-felt sender
  den nuværende forælder med — ellers ville et gem være en flytning"), som fanger regressionen hvis nogen bytter
  `getRawValue()` ud med `.value`. God test at pege på som mønster for fremtidige disabled-felter i formularer.
- "Kontakt enterprise arkitekten"-teksterne (person, dataobjekt) er PRÆCIS scopet til den situation, hvor knappen
  FØR var usynlig for alle ikke-admins, fordi kun admin kunne redigere formularen overhovedet — nu hvor en
  forvalter kan åbne formularen, ville den samme betingelse (`!canManagePersons()`/`!canManageDataObjects()`)
  ellers give en STUM sektion. Testet begge veje (hint væk når `me(true)`, til stede når `me(false)`).
- Ingen deling brudt: `/api/me.canCreateSystems` stadig admin-only (bekræftet BÅDE i kode — `Policies.CreateSystem`
  uændret admin-role-krav — OG live: Frida's `/api/me` viser `canCreateSystems:false`). Koblingsimport og
  kapabilitetsimport (`Policies.ManageCapabilities`) og dataobjekt-oprettelse (`Policies.ManageDataObjects`) er
  UÆNDREDE admin-only policies — 4a udvider kun `EditSystem`/`EditIntegration`/tilføjer `MoveSystem`, ingen anden
  policy er rørt. Person.EntraObjectId→Oid omdøbningen (beslutning T) er ren intern rename, IKKE eksponeret i
  nogen DTO (grep bekræftet), så ingen kontrakt-opdatering (openapi.json/schema.d.ts) var nødvendig — korrekt at
  PR'en ikke rører dem.
- `TestAccess.BindPersonAsync` binder oid direkte i testdatabasen (ingen ny API-flade) — bevidst, matcher det
  allerede noterede fund "INVITATION HAR INGEN UI" (stadig åbent, hører til 4b/F-serien ifølge skæringen, ikke 4a).
- Skæringen holder: 4a er PRÆCIS "O–S og omdøbningen i T (server) + 'Kontakt enterprise arkitekten' hvor en knap
  var skjult" — ingen historik (4c/W), ingen "Mine systemer" (4b), intet Firebase (F-serien) sneg sig med. `docs/
  plan.md`-diffen i denne branch er ren TILFØJELSE af beslutninger O–W (allerede committet FØR 4a-koden, i en
  tidligere commit på samme branch) — teksten i O/Q/R/S stemmer ord-for-ord med det, koden gør (verificeret ved
  citat-sammenligning, ikke kun stikprøve).
- MINDRE, IKKE BLOKERENDE: Ejerens spørgsmål 2 ("må en forvalter skifte forretningsejer? — ja") er IKKE begrænset
  særskilt nogen steder — `SystemWriteRequest.Roles` valideres kun af `ValidateRoles` (dubletter/SingleHolder), og
  enhver rolle (inkl. Forretningsejer) kan sættes af enhver, der har `EditSystem` på systemet. Det er PRÆCIS det
  ejeren bad om ("ja, som de andre roller"), men ingen ny test siger det EKSPLICIT for Forretningsejer-rollen (kun
  Systemejer/Systemforvalter/Forretningsejer er testet for HVEM DER KAN REDIGERE, ikke at en forvalter kan SÆTTE en
  ny forretningsejer). Lav risiko (samme kodesti som de andre roller, ingen særbehandling af Forretningsejer i
  `Apply()`), men spørg Test Manager om en eksplicit test for netop dette scenarie ved næste berøring af roller.

## Kode-gennemgang: SQL-hærdning (c6e9bed, PR #15, branch claude/trusting-brahmagupta-4v2ukj)
Konklusion: GOD AT LANDE, med ÉN SKAL-RETTES (tekst, ikke kode) og to IKKE-BLOKERENDE opfølgninger. Verificeret
ikke kun ved læsning, men ved RIGTIG mutation mod committet kode (bygget, ikke kun antaget, og rullet tilbage
bagefter): en `ExecuteSqlRawAsync($"…{userInput}…")` og en rå `NpgsqlCommand("…" + userInput)` indsat i `src/`
gav begge en ØJEBLIKKELIG build-fejl (EF1002 hhv. CA2100); en stray `db.ExecuteSqlAsync($"SELECT 1")` uden for
`TableLocks.cs` gjorde `SqlSafetyTests.SQL_tekst_findes_kun_i_den_faste_laaseliste` rød. Samme rå-SQL-kald
indsat i en migrations-fil (`Data/Migrations/…`) gav IKKE en fejl — `generated_code = true`-undtagelsen
virker også for de tre nye analyzer-ID'er, ikke kun for den gamle brede `dotnet_analyzer_diagnostic.severity`,
bekræftet empirisk (ikke kun antaget ud fra .editorconfig-rækkefølgen). 335/335 API-tests grønne,
`has-pending-model-changes` ren, `dotnet format` ren.
- Alle fire dele af ejerens krav er reelt indfriet, tre af dem I KODEN nu, én kun I PLANEN (bevidst):
  1. Parametre/ORM overalt: INGEN rå SQL tilbage i `src/` (grep-bekræftet); eneste SQL-tekst er
     `Data/TableLocks.cs` (en enum → `FormattableString`, ingen argumenter).
  2. Fast liste for identifikatorer: grep efter `OrderBy(`, `EF.Property`, `[FromQuery]`-sortering/felt-navne og
     CSV-header-håndtering (`CapabilityCsv.Header`, `CouplingCsv.Header`, `IntegrationCsv.Header`) viste INGEN
     andre steder, hvor et identifikator-agtigt input (kolonne/tabelnavn) bliver til SQL — `TableLock`-enummen
     var rent faktisk den ENESTE frie streng i hele API'et. Værd at gen-tjekke ved fremtidige "sortér efter
     kolonne fra query"-features.
  3. Mindste rettighed: KUN SKREVET NED (beslutning X i `docs/plan.md`), ikke bygget — lokalt er der stadig
     kun ÉN rolle (`ea`, CREATEDB, se `scripts/dev-db.sh`), ingen `ea_app`/`ea_migrator`/læserolle findes endnu.
     Beslutning X selv er PRÆCIS og korrekt afgrænset ("I driften får appen…", altså fremtid/produktion) — men
     CLAUDE.md's egen destillation af reglen ("Databasen giver appen mindste rettighed: kun DML; migreringer
     kører med en særskilt rolle") DROPPEDE kvalifikationen og lyder som en nutidig kendsgerning. ANBEFALET
     RETTELSE (ikke selv udført af QC): præcisér med "planlagt"/"i driften" (fx "får appen i driften mindste
     rettighed … (beslutning X, endnu ikke oprettet lokalt)"), ikke en påstået nutidig sandhed. Tjek ved næste
     læsning af CLAUDE.md, at teksten stadig matcher hvad der faktisk er bygget, ikke kun planlagt.
  4. Analyzer i CI: EF1002/CA2100/CA3001 sat til `error` i `.editorconfig`, kørt via det EKSISTERENDE
     `dotnet build`-trin i `ci.yml` (ingen ny CI-fil nødvendig for dette lag) — bekræftet med mutation. Plus en
     ny selvstændig CodeQL-workflow (`csharp` + `javascript-typescript`, `security-extended`).
- NYT MØNSTER (godt): race-testene (`CouplingTests`/`CouplingImportTests`) tager nu låsen via en DELT helper
  (`DbLocks.Lock` → `TableLocks.Sql(tableLock)`, PRODUKTIONENS kilde), i stedet for hver sin hardkodede
  SQL-streng som før. Først en bekymring: mutation af tabelnavnet i `TableLocks.Sql` ville nu ramme BÅDE
  produktionskoden og testens "modstander"-lås ens, og racetesten ville stadig bare se to (forkerte) låse
  kollidere — men det er OK, fordi den PRÆCISE SQL-tekst i forvejen har sin EGEN vagt ét sted
  (`SqlSafetyTests.Hver_laas_er_en_konstant_tekst_uden_argumenter`, literal streng-sammenligning) — races-
  testene tester derfor korrekt KUN blokerings-adfærden, ikke SQL-teksten igen. Præcis "én vagt pr. regel,
  samlet ét sted" fra CLAUDE.md's testprincipper. Genbrug denne adskillelse (én test for "er teksten rigtig",
  andre tests for "opfører systemet sig rigtigt med den") som mønster fremover.
- IKKE-BLOKERENDE, kunne ikke verificeres herfra (ingen adgang til rigtig GitHub Actions/API):
  - CodeQL `build-mode: none` for `csharp`: en nyere ekstraktionsmåde for et kompileret sprog — fornuftig
    begrundelse i kommentaren (undgår at skulle installere en bestemt .NET-version på runneren), men bør
    verificeres på den FØRSTE rigtige kørsel (kig efter "intet at analysere/no files extracted"-advarsler for
    csharp-jobbet), ikke antages at virke perfekt fra dag ét.
  - Fork-PR'er: `pull_request`-triggede workflows fra en ekstern fork får et SKRIVEBESKYTTET GITHUB_TOKEN,
    UANSET `permissions: security-events: write` i filen — SARIF-uploadet ville fejle (403) for en ægte
    ekstern bidragyders fork-PR, og CodeQL-tjekket ville stå rødt uden at det betyder noget om PR'ens indhold.
    Lav risiko nu (soloprojekt, ingen eksterne bidragydere), men værd at skrive ned et sted, så det ikke
    undersøges som en gåde senere.
  - Hvis repoet er PRIVAT (uklart fra CLAUDE.md, kun "personlig GitHub-konto"): code scanning/CodeQL-upload
    kræver historisk GitHub Advanced Security for private repos — hvis det ikke er slået til, ville
    analyze-uploadet fejle på HVER kørsel, ikke kun fork-PR'er. Spørg Release Manager om at bekræfte
    repo-synlighed og at "CodeQL" rent faktisk består én gang, før den evt. sættes som et krævet tjek i
    branch protection.
  - `docs/plan.md`'s F3-punkt ("container, `firebase.json` og engangskommandoerne") nævner IKKE eksplicit
    oprettelsen af `ea_app`/`ea_migrator`/læserollen, selvom beslutning X siger "Rollerne oprettes af ejerens
    script i F3/F4" — lille uklarhed for den, der senere bygger F3: tilføj en linje der eksplicit nævner
    rolle-scriptet, så det ikke overses.

## Plan-gennemgang 4b-1 (2026-09-25, main 7ddb8ba) — "Mine systemer", mail-links, advarsel. Tjek ved kode-gennemgangen:
(NB: kaldet henviste til mine "forslag om toggle + roller i stedet for adgangssæt" fra plan-gennemgang 4 — de stod
IKKE i denne fil. Skriv plan-forslag ned med det samme, ellers kan de ikke efterprøves næste gang.)
- LOGIN-LANDING: authGuard sætter ALTID returnUrl=state.url, og ''→redirect 'systemer' sker før vagten, så en
  normal indgang giver /login?returnUrl=%2Fsystemer. "Land på X medmindre returnUrl" fyrer derfor aldrig. Spørg
  altid: hvilken returnUrl har den ALMINDELIGE indgang? (web/src/app/core/auth.guard.ts, app.routes.ts)
- KOMPONENT-GENBRUG: system-list.page læser kun route.snapshot i ngOnInit. Et menulink /systemer → /systemer?mine=1
  (samme route) genbruger komponenten → URL skifter, listen gør ikke. Gælder ethvert nyt link til samme side med
  andre query-parametre. Også routerLinkActive (subset) gør "Systemer" og "Mine systemer" aktive samtidig.
- Minimal-API bool: klienten sender filter-objektet råt som query (systems.api.ts list) — `mine=1` fra URL'en giver
  400 mod `bool? mine`. Ét stavemåde hele vejen.
- "Aldrig bekræftet" FINDES IKKE: LastConfirmedAt er non-null og sættes ved oprettelse (Touch). Fremtidig fælde:
  Excel-import (6) vil stemple importerede rækker som "bekræftet nu" → ældst-først-sortering gemmer netop dem.
- Nedlagte systemer bliver aldrig bekræftet → flyder til toppen af "ældst bekræftede øverst" / "trænger mest til
  et blik". Spørg ved alle friskheds-sorteringer: hvad med Nedlagt?
- Modul-arv (beslutning O) i tekster: "Redigeres af systemejer og systemforvaltere" og "Kontakt systemforvalteren"
  ser kun modulets EGNE roller; forælderens forvalter (som også redigerer) står ikke i SystemDetail. Advarslen
  "kan derefter ikke redigere" er falsk for et modul, hvor man har ret via forælderen → skal afgøres af serveren.
- Tal i menuen fra /api/me er et øjebliksbillede (me() hentes én gang i vagten) → forældet efter egen rolleændring.
- Tal og liste skal bruge SAMME IQueryable-regel (mønster: query.Missing()) — ikke to implementeringer.

## Kode-gennemgang af 4b-1 (2026-09-25, PR #16, 17b36ba mod main 7ddb8ba) — status: LAND
Alle fund fra plan-gennemgangen er bygget som foreslået, verificeret manuelt (curl mod kørende API som frida/eva/lars
via dev-login, ikke kun læst i koden):
- B1 (landing): redirectTo 'systemer' sker FØR authGuard, så en almindelig indgang giver returnUrl=/systemer.
  landingUrl() behandler netop '/systemer' som "intet mål" og sender ikke-EA med roller til MINE_URL. Bekræftet:
  Frida (mySystemCount=5) → Mine systemer; Eva (EA.Admin) og Leo (ingen roller) → /systemer.
- B2 (URL-styret liste, udelukkende menupunkter): system-list.page abonnerer nu på route.queryParamMap (ikke kun
  snapshot i ngOnInit) — gælder ALLE filtre, ikke kun mine=true, så kapabilitetsfilter-links fra andre sider også
  virker ved komponent-genbrug. app.ts' onMine()/onSystems() er gensidigt udelukkende (onSystems kræver !onMine).
  Testet med positive assertions (app.spec.ts: hvilket link har klassen "active", ikke kun at et link findes).
- B3 (nedlagte sidst, "aldrig bekræftet" findes ikke): OrderBy(Nedlagt).ThenBy(LastConfirmedAt) i SystemEndpoints;
  LastConfirmedAt fortsat non-null overalt. MySystemsTests dækker rækkefølgen med 5 konkrete systemer.
  17b36ba (opfølgende commit) rettede selv en båndsvaghed: 5 systemer vs. 6 roller (to systemer havde to roller
  hver) — uden den skelnen havde en mutation af "roller" i stedet for "systemer" ikke slået ud (grøn med forkert
  regel). Godt eksempel på testprincippet "et bånd der rummer både gammel og ny værdi måler ingenting" — fundet og
  rettet AF opgaven selv, ikke af QC.
- B4 (editors/canEditViaParent): SystemEndpoints.EditorsOf gengiver beslutning O præcis (egne EditingRoles-bærere
  + forælderens, forretningsejer redigerer ikke, en person kun én gang, "via" sat af serveren — klienten kan intet
  gætte). SystemPermissions.CanEditViaParent styrer selfRemovalWarning korrekt: verificeret med curl at Nordlys HR
  (modul uden egen rolle for Frida) har canEdit=true, canEditViaParent=true, editors=[Frida via Nordlys ERP].
- Toggle "Kun mine systemer": bevarer andre filtre (queryParamsHandling: 'merge'); "Nulstil filtre" rydder mine
  filter med (bevidst, ét sted at komme ud af visningen).
- Én regel for menuens tal og listen: SystemQueries.Mine(IQueryable, ClaimsPrincipal) bruges UÆNDRET af både
  /api/me (MySystemCount) og /api/systems?mine=true — ikke to formuleringer af samme ting.
- loadMe efter gem: system-form.page.save() kalder auth.loadMe() efter et vellykket gem, så menuens tal ikke er en
  fastfrosset visning fra login (kendt fælde i denne hukommelse, nu netop håndteret ved den ene skrivevej, der kan
  ændre roller).
- Kontakttekster: "Redigeres af X" / "Ser noget forkert ud? Det rettes af X" / "Systemet har ingen systemejer eller
  -forvalter. Kontakt enterprise arkitekten." — alle tre testet med FULD streng (ikke kun DOM-tilstedeværelse),
  inkl. mailto-links og "(via Nordlys)"-suffiks.
- Advarsel ved tab af alle redaktører (beslutning P): noEditorsWarning gælder kun `!s.parent` (selvstændige
  systemer) — korrekt, for et modul kan forælderens redaktører stadig redigere det, selvom modulet mister sine
  egne. Testet eksplicit (system-form.page.spec.ts): modul + fjern egen rolle → INGEN no-editors-advarsel.
- "Din rolle" (domæne-rådgiveren): kolonne kun i mine-visning, viser roller eller "Via <forælder>" for et modul
  uden egen rolle (myRoles: null uden for mine=true, tom liste for et arvet modul — skelnes korrekt i UI'en).
- Kontrakt-kæden i sync: openapi.json ⇄ schema.d.ts verificeret med `npm run gen:api` (ingen diff). Ingen nye
  enum-værdier, så labels.ts krævede ingen ændring.
- Fiktive data: Frida/Bo/Hanne/Lars/Eva/Leo er alle mærket "(fiktiv)" i DevSeed/appsettings.Development.json, ingen
  DTU-navne eller -mails (alle @eksempel.invalid).
- Verificeret selv: `dotnet build`, alle 342 API-tests (rigtig PostgreSQL), 137 web-tests, `npm run lint`, og
  `npm run build` — alle grønne på committet kode (Node 24 krævet lokalt, .nvmrc siger 24; systemets default var 22).
Ikke-blokerende, kun observeret (ingen ny fejl introduceret af 4b-1, men værd at have i baghovedet næste gang listen
eller "Total" rammes): SystemListResponse.Total er FORTSAT "hele registret ufiltreret" (dokumenteret sådan siden før
4b-1) — gælder også mine=true, så "Viser 5 af 15 systemer" på Mine-siden IKKE betyder "5 af dine 15". Samme mønster
som alle andre filtre, ingen regression, men spørg ved en fremtidig ændring af tælleren, om det stadig er tydeligt
nok. "← Alle systemer"-linket på systemsiden peger stadig altid på det UFILTREREDE /systemer, uanset om man kom fra
"Mine systemer" — men teksten siger netop "Alle systemer", ikke "Tilbage", så den lover ikke mere end den holder.
