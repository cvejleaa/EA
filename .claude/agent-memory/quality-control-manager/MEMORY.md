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
