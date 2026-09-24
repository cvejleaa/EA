# Plan: EA-register (LeanIX-light)

Levende dokument. Opdatér det, når en beslutning ændres.

## Formål

Understøtte IT Forretningsløsningers enterprise arkitekt med et register, der
hurtigt kan svare på:

1. Hvilke systemer har vi, hvem ejer dem (forretningsejer, systemejer,
   -forvaltere), og hvilket team forvalter dem?
2. Hvad rammes, hvis system X går ned, udskiftes eller migreres?
3. Hvilke systemer kører på teknologi Y (version Z)? Det er grundlaget for
   sårbarheds- og EOL-opfølgning, fordi teknologi i dag ikke er registreret
   pr. system.
4. Hvilke systemer løser samme opgave (konsolideringskandidater)?
5. Hvor er SOP'en for X, og er den gennemgået?
6. Hvilke kontrakter har frister snart, og hvor mange licenser er der?
7. Hvilke systemer bryder arkitekturprincipperne (Comply-or-Explain)?

## Ejerens beslutninger (2026-09-24)

| # | Beslutning |
|---|---|
| 1 | Prototype med fiktive data i dette repo |
| 2 | .NET + Angular; Specialiserede Løsninger (Custom Solutions) kan overtage |
| 3 | Byg selv (LeanIX for dyrt; andre alternativer vurderet) |
| 4 | Hele DTU på sigt (~2000 systemer), ITFL først (~300) |
| 5 | Der findes en Excel-liste; import bygges, når filen er tilgængelig |
| 6 | "Enslydende systemer" = systemer, der løser samme opgave (funktionelt overlap) |
| 7 | Defender findes, men teknologi/kode pr. system er ikke registreret |
| 8 | Licenser: kun kontraktfrister og antal (licensopfølgning senere) |
| 9 | SOP'er skrives i værktøjet og linkes gerne til processer |
| 10 | Moduler som undersystemer; blanketter kun hvis forretningskritiske |
| 11 | Kapabilitetskort: HERM |
| 12 | Comply-or-Explain og sikkerheds-scorecard: fase 2 |
| 13 | Forvaltere redigerer egne systemer, EA kuraterer; sårbarheder/priser begrænset |
| 14 | Godkendt AI: Copilot |
| 15 | Tidsanker: foranalysen af DTU Basen |
| + | Forretningsvinklen pr. system: hvem er forretnings-/procesejer |

## Delopgaver (rækkefølge)

Hver delopgave kan landes alene og giver værdi for sig.

1. **Fundament og systemregister** *(landet i PR #1)*. Omfatter systemer, moduler,
   aliaser, livscyklus, type, forvaltende team, roller (forretningsejer,
   systemejer, systemforvaltere) og personer med afdeling. Dertil søgning,
   filtre, friskhed ("Bekræft uændret"), adgang på serveren, dev-login, CI og
   API-kontrakten.
2. **Integrationer og "hvad rammes"** *(landet i PR #2 og #3)*. Integrationer registreres med fra/til,
   type (API/fil/event/direkte-DB/udtræk), dataobjekter og CSV-eksport. CSV'en
   er også det fremtidige importformat og skal indeholde en matchnøgle (id
   eller ExternalKey). Den udleveres til foranalysen som skabelon. Lister og
   datatræk registreres som systemer af typen "Lokal løsning/udtræk".
3. **Kapabiliteter og funktionelt overlap** *(i gang; se beslutningerne
   nedenfor)*. Kapabilitetsmodellen importeres via CSV (HERM hos DTU; der
   ligger ingen HERM-tekst i repoet af hensyn til licensen). Overlap vises som
   kapabiliteter med 2 eller flere aktive systemer. Forælder og modul,
   planlagte udskiftninger (Indfases + Udfases) og udtræk tælles ikke som
   overlap.
4. **Forvaltere redigerer egne systemer og ændringshistorik**. Adgangen
   bindes til `oid` på rolletildelingen. Denne delopgave skal være landet, før
   der kommer rigtige data ind.
5. **Teknologi og EOL**. Produkt og version registreres pr. system, og der
   kan søges på "hvem bruger X?". EOL hentes via en adapter
   (endoflife.date/fixture). Tekniske detaljer får deres egen adgangspolicy.
6. **Excel-import**. Importen får kolonne-mapping og en tør-kørsel, der viser
   før og efter, og den er idempotent via ExternalKey.
7. **Processer og SOP'er**. SOP'er skrives i Markdown med uforanderlige
   versioner og en revisionsdato og kobles til systemer og processer.
8. **Leverandører og kontrakter**. Frister, opsigelsesvarsel og antal
   licenser, samt en visning af kommende frister.

**Spor A: Azure og Entra ID**. Kræver app-registreringer hos DTU IT, så den
proces bør startes nu. Det omfatter MSAL i klienten, migrations bundle før
appen, og at API'et serverer Angular-buildet.

## Beslutninger for delopgave 2: integrationer

Arkitektens plan blev gennemgået af Quality Control og domæne-rådgiveren før koden, og deres fund er
indarbejdet.

| # | Beslutning | Hvorfor |
|---|---|---|
| A | **Retning er dataflow** ("data går fra → til"). Der er intet retningsfelt; tovejs registreres som to rækker. | Svarer direkte på "hvad rammes". Et system, der henter via API, er *modtageren*. Det står i UI-teksterne og i CSV-vejledningen, fordi det er den mest sandsynlige fejlregistrering. |
| B | **Fra og til kan ikke ændres** efter oprettelse. | Giver en stabil identitet til import og undgår at flytte adgang mellem systemer i delopgave 4. |
| C | **Dublet-nøgle: (fra, til, type, platform, navn)**. NULL tæller som en værdi. Navnet er valgfrit. | Fanger den samme integration registreret to gange. Navnet adskiller reelle, separate flows, så importen kan matche entydigt. |
| D | **Ingen status på integrationen.** "Planlagt" og "nedlagt" udledes af endernes livscyklus, og sletning er hård, indtil ændringshistorikken kommer i delopgave 4. | Et felt kommer først, når en visning bruger det. Begrænsningen står i CSV-vejledningen. |
| E | **Tidsstempler på integrationen.** UpdatedAt sættes ved hver skrivning. | Uden den ville en ændring, der kun rører dataobjekterne, ikke blive tjekket for samtidige ændringer (QC). |
| F | **409 har en type**: forældet version, dublet eller blokeret. | Kun en forældet version må tilbyde "Hent nyeste version". Rettelsen gælder også systemformularen. |
| G | **Systemer, der er i brug, kan ikke slettes.** Det gælder både som ende og som platform, og begrundelsen viser antal. | Sletning må ikke fjerne historik. Brug status "Nedlagt". |

CSV-formatet er beskrevet i [`csv-integrationer.md`](csv-integrationer.md), med eksemplet
[`csv/integrationer-eksempel.csv`](csv/integrationer-eksempel.csv). Begge kan sendes til foranalysen nu, og
formatet er låst som ekstern kontrakt.

## Beslutninger for delopgave 3: kapabiliteter og overlap

Arkitektens plan blev gennemgået af Quality Control og domæne-rådgiveren før koden, og deres fund er
indarbejdet. Tre spørgsmål ligger hos ejeren (se nederst). Indtil der kommer svar, bygges der efter
forslagene.

| # | Beslutning | Hvorfor |
|---|---|---|
| A | **Kortet kommer kun ind via CSV-import, og filen er HELE kortet.** Manglende koder slettes, og når der findes koblinger (3b), markeres de *udgået* i stedet. Der er ingen redigering af kortet i brugerfladen. | Én kilde og ingen afdrift fra DTU's HERM-udgave. Det er det modsatte af integrations-CSV'en, og det står med fed i [`csv-kapabiliteter.md`](csv-kapabiliteter.md). |
| B | **Tør-kørsel og fingeraftryk.** Tør-kørslen viser før og efter (det, der fjernes, først), advarer når over 20 % af kortet fjernes, og afviser en tom fil. En gennemført import gemmer præcis det, tør-kørslen viste. Ellers svarer den 409 `stale-dry-run` med besked om at køre tør-kørslen igen. | En delvis fil må ikke tømme kortet. Kun en forældet *version* må tilbyde "Hent nyeste version", så tør-kørslen har sin egen konflikttype. |
| C | **Ét format med kode på alle rækker** (`Kode;Navn;ForælderKode;Beskrivelse`). Har DTU's udgave ikke koder på familier og grupper, giver EA dem en kode én gang. | To importformater koster mere end en engangsredigering *(ejerens spørgsmål 2)*. |
| D | **Kun blade kan få nye koblinger**. Kobling kan ligge på et modul eller på forælderen ("hele Nordlys gør X"). Vælgeren søger også på gruppens navn og viser stien. | Overlap giver kun mening på samme granularitet. Et ERP med 8 moduler skal ikke kobles 8 gange. |
| E | **Overlap-reglen (én funktion på serveren):** et system tæller med, når det er `Indfases` eller `IDrift` og ikke er en lokal løsning/udtræk. Forælder og moduler er ét system. Overlap betyder 2 eller flere systemer, der tæller med. `Udfases` tæller aldrig. `Planlagt` tæller ikke, men giver mærket "Planlagt: N", når der allerede er et aktivt system på kapabiliteten. | En truffet beslutning (udfasning) er ikke en kandidat. Et nyt system oven på et gammelt er dét, der skal fanges *(ejerens spørgsmål 1)*. |
| F | **Ordet "løsninger" bruges ikke på skærmen**, fordi det støder sammen med "Lokal løsning". Tallene hedder "N systemer" og "N kapabiliteter med overlap". Undtagelsens årsag er en kontrakt-enum med separate værdier for `Planlagt`, `Nedlagt`, `Udfases` og `LokalLoesning`. | Hvert tal skal have en entydig enhed. Enum-værdier omdøbes aldrig, så de deles op, før de lander. |
| G | **Dækning øverst på kortet**: "Kapabiliteter angivet for X af Y systemer — se de manglende". | Et tomt overlap skal kunne skelnes fra "ingen har koblet endnu". |
| H | **Udgåede kapabiliteter med koblinger får deres egen sektion** på kortet. Tør-kørslen viser, hvor mange koblinger en ny version flytter. | Oprydningsarbejdet efter en ny HERM-version må ikke fejle tavst. |

**Skæring** (hver skive lander alene, serveren før klienten):
- **3a:** model og import (server).
- **3a-web:** kort og import-side.
- **3b:** koblinger (server).
- **3b-web:** systemside, formular og filter.
- **3d:** import af koblinger fra regneark, hvis ejeren siger ja. Den skal ligge før 3c-web, så overlap ikke lander på tomme data.
- **3c:** overlap, koblings-CSV og dækning.

**Ejerens spørgsmål (forslag i parentes):**
1. Overlap-reglen som i E? *(ja)*
2. Må EA give familier og grupper en kode? *(ja)*
3. Kobler EA de ~300 systemer centralt via regneark (3d), mens formularen bruges til den løbende
   vedligeholdelse? *(ja)*

## Designprincipper for værktøjet selv

- **Fast metamodel.** Et felt kommer kun ind, når en navngiven visning bruger
  det, og nogen kan vedligeholde det. Der er ingen brugerdefinerede felter og
  ingen konfigurerbar metamodel; det er dem, der gør LeanIX stort.
- **Kun navn og status er obligatoriske.** "Ikke angivet" er ærligere end en
  tvunget forkert værdi, og filtre gør hullerne synlige.
- **Friskhed frem for completeness-score.** Hvert system viser, hvornår det
  sidst er bekræftet og af hvem. "Bekræft uændret" koster ét klik.
- **Serveren afgør adgang.** Klienten viser knapper ud fra server-beregnede
  `permissions` med begrundelse.
- **Data ved kilden.** Personer er i produktion en cache af Entra ID, og
  teams er seedet.

Værktøjet skal selv følge sektionens seks arkitekturprincipper:

- **Standard før skræddersyet:** vi bygger selv, fordi LeanIX var for dyrt, og
  det er dokumenteret ovenfor.
- **Data ved kilden:** personer fra Entra ID.
- **API First:** klienten bruger det samme API, som andre kan bruge.
- **Single Identity:** Entra ID.
- **Konsolidering før nyt:** ingen ny database-motor ud over PostgreSQL.
- **Driftbarhed før Go-Live:** Digital Arbejdsplads skal godkende
  driftsdesignet, før spor A går i produktion.

## Bevidst udeladt (indtil videre)

| Udeladt | Hvorfor |
|---|---|
| Kritikalitet | Kræver DTU's egen klassifikation; den opfindes ikke her |
| Login-metode, hosting, persondata | Fase 2 (Comply-or-Explain) |
| Grafvisning og transitiv konsekvensanalyse | Tabel og CSV rækker til foranalysen |
| CVE-feeds, Defender-kobling, Copilot/MCP | Fase 3 |
| Licensforbrug og priser | Ikke i scope (beslutning 8) |
| Mails om frister | Et pull-view i stedet, så intet fejler tavst |
| Team-administration i UI | Teams er seedet; nye teams kræver en migration |
| E2E-tests | Fra delopgave 4, hvor roller i UI'et gør dem værdifulde |
| Google Fonts/ikon-fonte | Ingen eksterne kald fra UI'et (privatliv); systemskrifter |
