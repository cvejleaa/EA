# CSV-skabelon: integrationer

Vejledning til den, der udfylder en oversigt over integrationer, lister og datatræk, fx en foranalyse. Filen
kan senere læses ind i EA-registret uændret. Et eksempel med fiktive data ligger i
[`csv/integrationer-eksempel.csv`](csv/integrationer-eksempel.csv).

> **Status:** Registret kan i dag *eksportere* i dette format. *Import* kommer i en senere delopgave, og den
> starter altid med en tør-kørsel, der viser før og efter, før noget gemmes. Formatet ændres ikke i
> mellemtiden.

## Det vigtigste: retning er dataflow

Hver række er ét **dataflow**: data går **fra** `DataFra` **til** `DataTil`.

- **Et system, der henter data via et API, er modtageren.** Eksempel: Laborant kalder identitetskildens
  API for at hente brugere. Data går *fra* identitetskilden *til* Laborant, så `DataFra` er
  identitetskilden og `DataTil` er Laborant. Teknikere siger ofte "Laborant kalder identitetskilden", men
  det, der tæller her, er, hvor data *ender*.
- **Tovejs-synkronisering er to rækker**, én for hver retning, hver med sine egne dataobjekter.
- **En platform er ikke en ende.** Går et flow gennem integrationsplatformen, skrives afsender og modtager
  i `DataFra`/`DataTil` og platformen i `ViaPlatform`. Skriv ikke to rækker (A → platform og platform → B),
  for så forsvinder B fra oversigten over, hvad A rammer.

## Kolonner

Første linje er overskrifterne, præcis som herunder og i samme rækkefølge. En tom celle betyder "ikke
angivet". Skriv aldrig teksten "Ikke angivet".

| Kolonne | Indhold |
|---|---|
| `Id` | Registrets id for integrationen. **Lad den stå tom for nye rækker.** Rækker fra en eksport har id'et udfyldt. Ret det ikke. |
| `ExternalKey` | Jeres egen nøgle for rækken, fx et løbenummer i foranalysen. Den bruges til at genkende rækken, hvis filen læses ind flere gange. |
| `Navn` | Valgfrit navn på flowet, fx "Natlig brugerfil". Brug det, når der er flere flows mellem de samme to systemer med samme type og platform. Ellers bliver de betragtet som den samme integration. |
| `DataFra` | Systemet, data sendes **fra**. Brug systemets præcise navn fra registret (se *Systemnavne* herunder). |
| `DataTil` | Systemet, data sendes **til**. |
| `Type` | En af typekoderne herunder, eller tom. |
| `ViaPlatform` | Platformen, flowet går igennem, fx integrationsplatformen. Tom, hvis flowet går direkte. |
| `Dataobjekter` | Hvilke slags data, adskilt af ` \| `, fx `Medarbejder \| Organisationsenhed`. Tegnet `\|` må ikke indgå i et navn. Nye navne foreslås oprettet ved import (vises i tør-kørslen). |
| `Beskrivelse` | Fritekst: hvad, hvornår og hvorfor. |
| `DataFraId` | Registrets id for `DataFra`. Udfyldt i eksporter. Kan være tom i nye rækker. |
| `DataTilId` | Registrets id for `DataTil`. |
| `ViaPlatformId` | Registrets id for platformen. |

### Typekoder (`Type`)

| Kode | Betyder |
|---|---|
| `Api` | Et API-kald (REST, SOAP o.l.). |
| `Fil` | En fil, der overføres (fx en natlig fil via SFTP). |
| `Event` | En besked eller et event (kø, webhook, publish/subscribe). |
| `DirekteDb` | Direkte læsning eller skrivning i et andet systems database. Det er et **brud på arkitekturprincip 3** (API First) og skal registreres, så det kan findes. |
| `Udtraek` | Et udtræk, typisk til et regneark eller en lokal liste. |

## Systemnavne

- **Brug de præcise navne.** Hent referencelisten fra registret (`GET /api/systems/export.csv`; en
  knap i brugerfladen følger). Kolonnen `FuldtNavn` er den tekst, der skal stå i `DataFra`, `DataTil`
  og `ViaPlatform`. Listen har også kolonnerne `Id`, `Navn`, `Forælder`, `Aliaser`, `Type` og `Status`.
- **Moduler skrives `Forælder > Modul`**, fx `Nordlys ERP > HR`, fordi modulnavne kun er entydige inden
  for forælderen. Brug tegnet `>`.
- **Aliaser matcher også.** Et navn, der svarer til et systems alias, bliver genkendt. Tvetydige navne
  vises som fejl i tør-kørslen.
- **Hvis navn og id er uenige** i samme række (fx `DataFra` peger på ét system og `DataFraId` på et andet),
  er det en fejl i tør-kørslen. Den løses aldrig stille.

### Lister, regneark og datatræk, der ikke findes i registret

Det er ofte netop dem, en foranalyse skal finde. Registrér dem sådan:

1. **Listen eller regnearket er et system.** Skriv et beskrivende navn i `DataTil`, fx
   `Lønudtræk-regneark (lønkontoret)`. Findes navnet ikke i registret, **foreslås det oprettet som et system
   af typen "Lokal løsning/udtræk"** i tør-kørslen, og enterprise arkitekten godkender det.
2. **Flowet dertil er en integration** med `Type` = `Udtraek`.

Eksempelfilen viser præcis det mønster (`Månedligt lønudtræk`).

## Det, formatet ikke kan udtrykke endnu

- **Status på en integration** (planlagt, under udfasning, slukket). Registret udleder "planlagt" og
  "nedlagt" af de to systemers livscyklus. En *ny* integration mellem to systemer i drift, eller en, der
  skal erstattes (fx `DirekteDb` → `Api`), beskrives i `Beskrivelse`, fx "Planlagt 2027" eller "Skal
  erstattes af API".
- **Frekvens** (dagligt, løbende): skriv den i `Beskrivelse`.

## Import: hvad sker der

- Rækker matches i rækkefølgen `Id`, derefter `ExternalKey`, derefter de fire felter `DataFra`, `DataTil`,
  `Type` og `ViaPlatform` sammen med `Navn`.
- **Rækker, der ikke står i filen, slettes aldrig.** En eksport fra ét system indeholder kun det systems
  integrationer, og det er fint at sende en delmængde tilbage.
- **Apostroffer mod formler.** Registret sætter `'` foran `=`, `+`, `-` og `@` (også fuldbredde-varianterne
  `＝ ＋ － ＠`), når tegnet står først i et felt eller lige efter et komma eller en tabulator. Det sker
  også, når feltet starter med en tabulator eller et linjeskift, og foran en apostrof, der allerede står
  dér, så importen kan skelne registrets apostroffer fra dine egne. Formålet er, at et regneark aldrig
  tolker tekst fra registret som en formel, heller ikke hvis filen åbnes med komma som skilletegn. Det
  samme gælder efter et linjeskift og efter citationstegn eller mellemrum dér. En bindestreg foran et
  mellemrum, fx `100,- kr.`, får ikke apostrof. Står bindestregen sidst i feltet, fx `pris 100,-`, får den
  en, fordi feltets afsluttende citationstegn ellers kan starte en formel. Importen fjerner
  apostrofferne igen.

## Excel

- Filen er **UTF-8 med BOM** og bruger **semikolon** som separator, så den åbner korrekt med æøå i dansk
  Excel ved dobbeltklik.
- **Gem altid som "CSV UTF-8 (kommasepareret) (*.csv)"**, selv om navnet siger komma. Dansk Excel bruger
  alligevel semikolon. Vælger du i stedet "CSV (semikolonsepareret)", gemmes filen i en ældre tegnkodning,
  og æøå går tabt.
- Rediger ikke kolonneoverskrifterne, og bevar kolonnernes rækkefølge.
