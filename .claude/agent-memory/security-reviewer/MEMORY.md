# Security Reviewer – hukommelse (EA-registret)

## Adgangsmodel (bekræftet d646a21)
- Bearer-JWT, HS256 symmetrisk dev-nøgle. Dev-nøgle er committet i
  appsettings.Development.json (fiktiv, kun Dev/Testing – acceptabelt).
- FallbackPolicy kræver login på alt ikke-AllowAnonymous. Allow-liste
  håndhæves af AuthorizationTests (begge veje). Nye endpoints fanges.
- Skrive-policies: CreateSystem/ManagePersons = RequireRole(EA.Admin).
  EditSystem = resource-handler, checker p.t. KUN IsInRole(Admin)
  (resource ignoreres endnu; Delopgave 4 tilføjer ejer-tjek).
- Læsere må læse alt (GET/list åbne for alle indloggede) – bevidst.

## Bekræftet lukket (PoC kørt mod live API 2026-09-24)
- alg=none forfalsket admin-token -> 401.
- HS256 med forkert nøgle -> 401.
- Udløbet token (exp > 5 min i fortiden) -> 401. NB: default ClockSkew 5 min,
  så exp kun 100s i fortiden gav stadig 200 (forventet, ikke fund).
- Forkert issuer / forkert audience -> 401.
- Læser: create/update/confirm/delete system + create person -> alle 403,
  ingen data ændret.
- Startup-vagt: Auth:Mode=Dev i Staging OG Production -> InvalidOperationException,
  appen crasher (fejler lukket). Production+Entra -> NotSupportedException.
  VIGTIGT ved test: `dotnet run` uden --no-launch-profile tvinger
  ASPNETCORE_ENVIRONMENT=Development via launchSettings og maskerer vagten.
  Brug ALTID --no-launch-profile når du tester miljø-vagten.
- ILIKE: q med %/_/SQL-payload er parametriseret + escaped -> ingen injektion.
- Fejlsvar: kun ProblemDetails (title/traceId), ingen stacktrace.
  UseExceptionHandler (ikke DeveloperExceptionPage) selv i Development.
- openapi kun mapped i Development/Testing. Angular: ingen innerHTML/bypass/
  href-sinks – interpolation auto-escaper systemnavn/beskrivelse.
- returnUrl (login.page.ts): kræver ledende '/' og blokerer '//'; navigateByUrl
  er client-side -> ingen open redirect.

## Åbne noter (ikke-blokerende)
- Oracle: PUT/DELETE/confirm giver 404 (findes ikke) vs 403 (ingen adgang).
  Lækker eksistens, men læsere kan allerede liste alt -> ingen reel læk NU.
  Genovervej når læseadgang engang begrænses.
- Dyr operation før billigt tjek: UpdateSystem kører LoadAggregate (4 includes,
  split query) FØR EditSystem-tjekket, der p.t. kun er et rolle-tjek.
  Bryder CLAUDE.md "tjek før dyre operationer". Delopgave 4 skal bruge
  resursen, så re-evaluer da (evt. billig projektion/rolle-shortcut først).
- Malformet JSON -> 500 (burde være 400). Ingen læk, ren robusthed.

## Delopgave 2 (a0caef1, integrationer/dataobjekter/CSV) – kørt 2026-09-24
- BEKRÆFTET lukket: anonym -> 401 på alle nye ruter. Læser (lars) og
  rolleløs (frida): POST/PUT/DELETE integration + POST dataobjekt -> 403,
  intet ændret. Adgangstjek ligger før DB-validering (100k GUIDs + 100k-tegns
  navn som læser -> 403 på ~60 ms). Kun "fra/til mangler" giver 400 før 403
  (bevidst billigt tjek, ingen læk).
- EditIntegrationHandler: p.t. KUN IsInRole(Admin), resursen ignoreres.
  Delopgave 4 udvider -> genangrib (ikke-gemt resurs ved create: fra/til/via
  kommer fra klienten, så ejer-tjek skal ske på de rigtige systemer).
- q på /api/data-objects: % _ \ og SQL-payload -> parametriseret, ingen injektion.
- Content-Disposition: Csv.Slug -> kun [a-z0-9-]. Systemnavn med CRLF gav et
  rent filnavn. Lukket.
- FUND (BEKRÆFTET, ikke blokerende): CSV-formelvagten tjekker kun feltets
  FØRSTE tegn og citerer ikke felter med ','. Med ',' som skilletegn
  (fx engelsk Excel/LibreOffice) bliver et felt som `navn,=1+1` til en celle,
  der starter med '='. Ramte både integrations- og systemeksporten (navn,
  beskrivelse, dataobjekter, systemnavne). ';'-parse var ren (kontroltest).
  Rettelse: citér også felter med ',' og TAB (eller citér alle felter).
  SKAL lukkes før delopgave 4 (ikke-admin-skrivning) / 6 (import).
- FUND (BEKRÆFTET, lav): JsonStringEnumConverter accepterer heltal ->
  "type": 99 blev gemt som '99' og sendes tilbage som tallet 99 (kontrakt
  brudt, klientens labels giver undefined). Kun admin nu. Rettelse:
  JsonStringEnumConverter(allowIntegerValues: false) eller Enum.IsDefined.
  Gælder formentlig også systemers enums (fra før).
- Fuld eksport = hele angrebskortet i ét kald for enhver indlogget. Ikke nyt
  (læsere ser alt), men overvej revisionslog pr. eksport når der kommer rigtige data.
- NB: sikkerhedsklassifikatoren stoppede et PoC med rigtige exploit-strenge
  (DDE/cmd, WEBSERVICE-exfil). Brug KUN harmløse markører (=1+1) til
  CSV-test – det er nok til at bevise, at en celle bliver til en formel.

## PoC-mønstre (genbrug)
- CSV-injektion: eksportér, parse med python csv.reader BÅDE delimiter ';'
  og ',', led efter celler der starter med = + - @ (filtrér tomme celler fra).
- Python-hjælper req(m,p,tok,body) med urllib + tokens fra /api/dev/token
  (eva=admin, frida/lars=ingen roller).
- HS256-token i bash: header/payload base64url, openssl dgst -sha256 -hmac KEY.
- Kør API på egen port+DB: ConnectionStrings__Ea + --urls, ryd op med dropdb.
