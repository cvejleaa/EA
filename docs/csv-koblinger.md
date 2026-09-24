# CSV-format: koblinger mellem systemer og kapabiliteter

Vejledning til enterprise arkitekten og andre, der henter koblingerne fra EA-registret. Filen hentes med
**"Hent koblinger (CSV)"** på siden Kapabiliteter. Et eksempel med fiktive data ligger i
[`csv/koblinger-eksempel.csv`](csv/koblinger-eksempel.csv).

Filen bruges til tre ting:

- **Arbejdslisten til at koble systemerne.** Hvert system, der endnu ikke er koblet til en kapabilitet, står på
  en række uden kode.
- **Overlap-samtalerne med systemejerne.** Filtrér på `Overlap` = `Ja`. Så står det på hver række, hvem
  kapabiliteten deles med, og hvem der ejer systemet.
- **At koble mange systemer på én gang.** Ret koderne i Excel, og indlæs filen igen (se *Indlæs filen igen*
  herunder). Formularen på systemsiden bruges til den løbende vedligeholdelse.

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

## Indlæs filen igen

**Hent koblingerne, ret dem i Excel, og indlæs filen igen** med "Importér koblinger" på siden Kapabiliteter.
Start altid fra en frisk eksport.

### Det vigtigste: filen er hele sandheden for systemerne i den

- **For hvert system, der står i filen, bliver koblingerne præcis dem, der står i filen.** Koblinger, systemet har
  i dag, men som ikke står i filen, fjernes.
- **Systemer, der slet ikke står i filen, røres ikke.** Du kan derfor dele filen op, fx pr. team, og indlæse
  delene hver for sig. Tør-kørslen viser, hvilke systemer med koblinger der ikke står i filen.
- **Tøm koden, slet ikke rækken.** Vil du fjerne en kobling, så tøm cellen i `Kode`. Sletter du alle et systems
  rækker, står systemet ikke længere i filen og røres ikke. En række uden kode betyder "systemet er med i filen".
  Står den alene, fjernes alle systemets koblinger.

Det er det modsatte af kortets fil ([`csv-kapabiliteter.md`](csv-kapabiliteter.md)), som altid er hele kortet.

### Kun `SystemId` og `Kode` indlæses

- **Tilføj en kobling:** skriv koden i systemets række uden kode, eller kopiér en af systemets rækker og skriv den
  nye kode. `SystemId` skal stå på rækken. Et system, der ikke står i filen, fx et modul, der er dækket af
  forælderen, finder du med "Hent systemliste (CSV)" på systemlisten.
- **Flyt en kobling:** overskriv den gamle kode med den nye.
- **Et moduls koblinger** står på modulets egne rækker. En forælders rækker rører ikke modulerne.
- `FuldtNavn` er en kontrol: står der et navn, skal det passe til systemet. Er systemet omdøbt, så ret navnet
  eller tøm cellen.
- **Alle andre kolonner indlæses ikke.** Rettes fx `Status` eller `Forretningsejer` i filen, gemmes det ikke, og
  tør-kørslen siger det. Ret den slags på systemsiden.

### Tør-kørslen

Tør-kørslen viser, hvad importen vil gøre, før noget gemmes: det, der fjernes, står øverst, derefter det, der
tilføjes. Den advarer, når

- over en femtedel af koblingerne på systemerne i filen fjernes (en delvis fil?);
- et system er ændret i registret, efter filen blev hentet (`SidstÆndret`). Så kan det, der fjernes, være
  tilføjet af en anden siden. Tjek det, eller hent en ny eksport;
- en kolonne, der ikke indlæses, er rettet.

Importen gemmer præcis det, tør-kørslen viste. Er koblingerne eller systemerne i filen ændret i mellemtiden,
afvises importen med besked om at køre tør-kørslen igen. Alt gemmes samlet, eller intet. En import ændrer ikke
"Sidst bekræftet": det er forvalterens udsagn om, at oplysningerne er rigtige.

### Regler og grænser

Er der fejl, gemmes intet. Tør-kørslen viser hver fejl med linje og kolonne. Registret afviser blandt andet:

- forkerte eller omrokerede kolonneoverskrifter (alle kolonner skal stå der, også de beregnede);
- et `SystemId`, der mangler, er ugyldigt eller ikke findes i registret;
- et `FuldtNavn`, der ikke passer til systemet;
- en kode, der ikke findes i kortet, eller den samme kode to gange for samme system;
- en NY kobling til en kapabilitet, der ikke kan vælges (en gruppe eller en udgået). En kobling, systemet
  allerede har, bevares, også når kapabiliteten siden er udgået;
- højst 100 koblinger pr. system;
- en fil over 64 MB. En fil kan have højst 20.000 rækker; del en større fil op efter system.

Kun enterprise arkitekten (rollen *EA.Admin*) kan indlæse filen.

## Apostroffer mod formler

Som i de andre CSV-filer sætter registret `'` foran `=`, `+`, `-` og `@`, når tegnet står først i et felt
eller lige efter et komma, en tabulator eller et linjeskift (se `csv-integrationer.md`). Så kan Excel ikke
tolke en systembeskrivelse som en formel. Importen fjerner præcis de apostroffer igen.

## Excel

- Filen er **UTF-8 med BOM** og bruger **semikolon** som separator. Dansk Excel åbner den direkte.
- Filtrér og sortér gerne, fx på `Overlap`, `ForvaltendeTeam` eller `Status`.
- Gemmer du filen, så gem som **"CSV UTF-8 (kommasepareret) (*.csv)"**, ellers går æøå tabt, og filen afvises.
- Rediger ikke kolonneoverskrifterne, og slet ikke kolonner.
