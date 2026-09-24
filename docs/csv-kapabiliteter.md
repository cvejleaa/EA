# CSV-format: kapabilitetskortet

Vejledning til den, der vedligeholder kapabilitetskortet (DTU's udgave af HERM). Kortet kommer **kun** ind i
EA-registret via denne fil. Det kan ikke redigeres i brugerfladen. Et eksempel med fiktive data ligger i
[`csv/kapabiliteter-eksempel.csv`](csv/kapabiliteter-eksempel.csv).

> **Der ligger ingen HERM-tekst i dette repo.** HERM er licensbelagt. DTU's fil lægges kun ind i den kørende
> installation og committes aldrig.

## Det vigtigste: filen er HELE kortet

**En import erstatter hele kortet.** Kapabiliteter, der står i filen, oprettes eller opdateres. **Koder, der
ikke står i filen, fjernes.** Det er det modsatte af integrations-CSV'en, hvor manglende rækker aldrig
slettes.

- **Start altid fra en eksport.** Hent kortet fra registret, ret i Excel, og indlæs hele filen igen.
- **Tør-kørslen viser forskellen, før noget gemmes.** Det, der fjernes, står øverst. Fjerner filen mere end
  en femtedel af kortet, får du en tydelig advarsel. Det tyder på en delvis fil.
- **En tom fil afvises.** Det samme gælder en fil med kun overskrifter, for den ville fjerne hele kortet.

### Når systemer er koblet til kapabiliteterne

En kapabilitet, der fjernes, **slettes kun, hvis ingen systemer er koblet til den**. Ellers:

- **Den udgår.** Den forsvinder fra kortet og fra eksporten, men koblingerne bevares. Den kan ikke vælges til
  nye koblinger. Den står på kortets liste over **koblinger, der bør flyttes**, sammen med de systemer, det
  gælder, og stien til, hvor den sad. Ved systemet er koblingen markeret som udgået.
- **Den genaktiveres, hvis koden står i en senere fil.** Så kan en fejlimport rulles tilbage uden at miste
  koblinger.
- **En udgået kapabilitet uden koblinger slettes** ved næste import, hvor den ikke står i filen.
- **Et koblet blad, der får underkapabiliteter**, meldes i tør-kørslen. Koblingerne bevares, men bør flyttes ned
  på det blad, der passer bedst, for kun blade kan vælges. Det står også på listen over koblinger, der bør
  flyttes, indtil koblingerne er flyttet.

Tør-kørslen tæller koblingerne, der bør flyttes, fx "37 koblinger på 21 systemer", så prisen for en ny udgave af
kortet ses, før der trykkes. Advarslen om en delvis fil tæller kun det, der forsvinder fra kortet: oprydning af
allerede udgåede kapabiliteter tæller ikke med. **Omdøber en ny HERM-udgave en kode**, bliver det til "udgår + ny". Flyt så
koblingerne fra den udgåede kapabilitet til den nye.

## Kolonner

Første linje er overskrifterne, præcis som herunder og i samme rækkefølge. Hver række er én kapabilitet,
uanset niveau.

| Kolonne | Indhold |
|---|---|
| `Kode` | Kapabilitetens kode, fx `LT040`. **Påkrævet på alle rækker**, også familier og grupper, og unik uden hensyn til store og små bogstaver. Højst 40 tegn. Koden er importens nøgle: samme kode betyder samme kapabilitet. |
| `Navn` | Navnet, som det skal vises. Påkrævet, højst 200 tegn. |
| `ForælderKode` | Koden på kapabiliteten ovenover i træet. **Tom for det øverste niveau** (familierne). Forælderen skal stå i samme fil, men det er ligegyldigt hvor. |
| `Beskrivelse` | Valgfri forklaring, højst 4000 tegn. Den vises, når et system skal kobles til en kapabilitet, så den hjælper med at vælge rigtigt. |

### Koder på familier og grupper

Har DTU's HERM-udgave kun koder på selve kapabiliteterne, så **giv familier og grupper en kode én gang** i
regnearket, fx en forkortelse som `LT` eller `LT-STUD`. Behold den samme kode fremover, ellers bliver det
til "slettes + ny".

### Niveauer og rækkefølge

- **Niveauer:** kortet må have højst 4 niveauer, fx familie → gruppe → kapabilitet → DTU's egen underopdeling.
- Rækkefølgen i filen er ligegyldig. Registret viser søskende efter kode, hvor tal sammenlignes som tal, så
  `K1.2` kommer før `K1.10`.
- En eksport skriver altid forælderen før dens børn.

## Fejl i filen

Tør-kørslen viser hver fejl med linjenummer. **Er der fejl, gemmes intet.** Det gælder også, hvis kun én
række er forkert. Registret afviser blandt andet:

- en fil, der ikke er UTF-8 (se *Excel* herunder);
- forkerte eller omrokerede kolonneoverskrifter;
- manglende kode eller navn;
- den samme kode to gange;
- en `ForælderKode`, der ikke findes i filen;
- en forælder-kæde, der går i ring;
- mere end 4 niveauer;
- en fil over 2 MB eller med over 5000 rækker.

## Når importen gennemføres

- Registret gemmer **præcis det, tør-kørslen viste**. Er kortet eller koblingerne ændret i mellemtiden,
  afvises importen med besked om at køre tør-kørslen igen.
- Alt gemmes samlet. En halv import kan ikke ske.
- Kun enterprise arkitekten (rollen *EA.Admin*) kan importere.

## Apostroffer mod formler

Som i de andre CSV-filer sætter registret `'` foran `=`, `+`, `-` og `@`, når tegnet står først i et felt
eller lige efter et komma, en tabulator eller et linjeskift (se `csv-integrationer.md`). Det sker også
foran en apostrof, der allerede står dér.
**Importen fjerner præcis de apostroffer igen**, så en eksport, der indlæses uændret, giver nul ændringer.

## Excel

- Filen er **UTF-8 med BOM** og bruger **semikolon** som separator.
- **Gem altid som "CSV UTF-8 (kommasepareret) (*.csv)"**, selv om navnet siger komma. Dansk Excel bruger
  alligevel semikolon. "CSV (semikolonsepareret)" gemmer i en ældre tegnkodning, og den afvises, fordi
  æøå ellers går tabt.
- Rediger ikke kolonneoverskrifterne.
