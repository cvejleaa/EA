# CSV-format: koblinger mellem systemer og kapabiliteter

Vejledning til enterprise arkitekten og andre, der henter koblingerne fra EA-registret. Filen hentes med
**"Hent koblinger (CSV)"** på siden Kapabiliteter. Et eksempel med fiktive data ligger i
[`csv/koblinger-eksempel.csv`](csv/koblinger-eksempel.csv).

Filen bruges til to ting:

- **Arbejdslisten til at koble systemerne.** Hvert system, der endnu ikke er koblet til en kapabilitet, står på
  en række uden kode.
- **Overlap-samtalerne med systemejerne.** Filtrér på `Overlap` = `Ja`. Så står det på hver række, hvem
  kapabiliteten deles med, og hvem der ejer systemet.

Filen kan endnu ikke indlæses igen. Det kommer i en senere version, og så bliver `SystemId` og `Kode` nøglerne.

## Rækkerne

- **Én række pr. kobling.** Et system med tre kapabiliteter står på tre rækker.
- **Et moduls koblinger står på modulets egne rækker.** Kobler du "hele systemet" til en kapabilitet, står
  koblingen på forælderens række.
- **Én række uden kode pr. system, der mangler.** Det er de systemer og moduler, der hverken selv, gennem
  forælderen eller (for en forælder) gennem et af modulerne er koblet til noget. Det er samme regel som
  "Ikke angivet" på systemlisten. Nedlagte systemer står ikke på listen, for de skal ikke kobles.
- Rækkerne er sorteret efter `FuldtNavn` og derefter kode. Tal i koden sammenlignes som tal.

## Kolonner

| Kolonne | Indhold |
|---|---|
| `SystemId` | Systemets id i registret. Rediger det ikke. |
| `FuldtNavn` | Systemets navn. Et modul står som "Forælder > Modul". |
| `Kode` | Kapabilitetens kode. Tom på en række for et system, der mangler. |
| `Kapabilitet` | Kapabilitetens navn. |
| `Sti` | Niveauerne over kapabiliteten, fx "Uddannelse › Studieadministration". For en udgået kapabilitet: hvor den sad. |
| `Forælder` | Forældersystemets navn, når systemet er et modul. |
| `Status` | Systemets livscyklus: `Planlagt`, `Indfases`, `IDrift`, `Udfases` eller `Nedlagt`. |
| `Type` | Systemets type, fx `Saas`, `Egenudviklet` eller `LokalLoesning` (lokal løsning/udtræk). |
| `ForvaltendeTeam` | Det IT-team, der forvalter systemet. |
| `Forretningsejer` | Den i forretningen, der ejer processen, systemet understøtter. |
| `Systemejer` | Systemets ejer. |
| `SidstBekræftet` | Datoen, hvor nogen sidst bekræftede, at systemets oplysninger er rigtige. |
| `SidstÆndret` | Tidspunktet, hvor systemets data sidst blev ændret (UTC, fuld præcision). En bekræftelse ændrer det ikke. |
| `Overlap` | `Ja`, når to eller flere systemer, der tæller med, er koblet til kapabiliteten. `Nej`, når der ikke er overlap. Tom, når kapabiliteten ikke vurderes (se `BørFlyttes`) eller rækken ikke har en kode. |
| `SystemerDerTæller` | Antal systemer, der tæller med i overlap. Et system og dets moduler tæller som ét system. |
| `AntalPlanlagte` | Antal systemer på kapabiliteten, der kun er med som planlagte. |
| `TællerIkkeMed` | Hvorfor systemet på rækken ikke tæller med i overlap (se koderne herunder). Tom, når det tæller med. |
| `DelesMed` | De andre systemer, der er koblet til samme kapabilitet, adskilt af ` \| `. Står der en kode i parentes, tæller systemet ikke med, fx "Ugle (Udfases)". Rækkens eget system og dets moduler står der ikke. |
| `BørFlyttes` | Sat, når koblingen bør flyttes til en anden kapabilitet (se koderne herunder). Så vurderes overlap ikke. |
| `Systembeskrivelse` | Systemets beskrivelse fra registret. Brug den til at afgøre, om to systemer reelt løser samme opgave. |

## Hvem tæller med i overlap

Et system tæller med, når det er i drift (`IDrift`) eller under indfasning (`Indfases`) og ikke er en lokal
løsning/udtræk. **Et system og dets moduler tæller som ét system**, også to moduler i samme system.
Overlap betyder, at to eller flere systemer tæller med.

Koderne i `TællerIkkeMed`:

| Kode | Betyder |
|---|---|
| `Planlagt` | Systemet er endnu ikke i brug. Det tæller ikke, men står i `AntalPlanlagte`. Et planlagt system oven på et system i drift er netop det, der skal tales om, før der købes nyt. |
| `Udfases` | Systemet eller dets forældersystem udfases. Beslutningen er truffet, så det er ikke en kandidat. |
| `Nedlagt` | Systemet eller dets forældersystem er nedlagt. |
| `LokalLoesning` | Systemet er en lokal løsning eller et udtræk. |

Gælder flere, vinder status over type, og type over `Planlagt`: et udtræk, der udfases, står som `Udfases`.

Koderne i `BørFlyttes`:

| Kode | Betyder |
|---|---|
| `Udgaaet` | Kapabiliteten står ikke længere i kortet. Flyt koblingen til den kapabilitet, der har afløst den. |
| `HarUnderkapabiliteter` | Kapabiliteten har fået underkapabiliteter. Flyt koblingen ned på den, der passer bedst. |

Indtil koblingen er flyttet, indgår den ikke i overlap. Kortet har en liste over koblinger, der bør flyttes.

## Apostroffer mod formler

Som i de andre CSV-filer sætter registret `'` foran `=`, `+`, `-` og `@`, når tegnet står først i et felt
eller lige efter et komma, en tabulator eller et linjeskift (se `csv-integrationer.md`). Så kan Excel ikke
tolke en systembeskrivelse som en formel.

## Excel

- Filen er **UTF-8 med BOM** og bruger **semikolon** som separator. Dansk Excel åbner den direkte.
- Filtrér og sortér gerne, fx på `Overlap`, `ForvaltendeTeam` eller `Status`.
- Gemmer du filen, så gem som **"CSV UTF-8 (kommasepareret) (*.csv)"**, ellers går æøå tabt.
