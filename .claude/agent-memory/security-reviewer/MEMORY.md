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

## Delopgave 3a (840d4c4, kapabilitetskort + CSV-import) – kørt 2026-09-24
- BEKRÆFTET lukket (Kestrel, raw socket): læser/anonym med Content-Length
  100 MB og INGEN krop -> 403/401 på 3-64 ms, dvs. kroppen læses ikke før
  policyen. Admin: CL=3 MB -> 400 straks; chunked 3 MB -> 400 efter 2 MB.
  Læser: tør-kørsel og gennemførelse 403, intet gemt. Export/GET åbne for
  læsere (bevidst). Ingen cookies/CORS i src -> CSRF irrelevant.
- Fingeraftryk = SHA256 af Changes (ikke HMAC). Ikke en adgangskontrol, kun
  optimistisk samtidighed: samme ændringsliste = samme effekt, så forfalskning
  giver intet. 8 samtidige commits fra samme tilstand -> præcis 1x 200, 7x 409.
  LOCK TABLE SHARE ROW EXCLUSIVE blokerer ikke SELECT.
- FUND (BEKRÆFTET, admin-only DoS): TreeErrors går hele kæden (ikke stop ved
  MaxDepth) med List.Contains -> O(n^3). Lineær kæde 1000=3,5 s, 2000=25 s,
  5000 > 100 s (TestServer-timeout). Ring 2000 x 40-tegns koder: 75 s, +390 MB
  RSS, 18 MB svar (hver fejl rummer hele ringen). Parse ignorerer ct.
- FUND (BEKRÆFTET på python-csv, Excel formodet): formelvagten dækker ikke
  (a) linjeskift i felt (',' eller TAB-parse: "tekst\n=1+1," -> celle =1+1),
  (b) citationstegn mellem skilletegn og formeltegn ('x,"=1+1,' -> celle
  =1+1, fordi " fordobles og åbner et citeret felt i ','-parse). ';'-parse
  ren (0 af 50.000 fuzz-værdier). Rammer ALLE eksporter (fælles Csv.Field).
  SKAL lukkes før delopgave 4 (ikke-admin skriver).
- NUL-byte (\u0000) i felt: tør-kørsel 200, commit 500 (Postgres afviser),
  generisk ProblemDetails, intet gemt. Robusthed.
- Read(Write(x)) != x ved tomme/whitespace-linjer i citerede felter
  (TextFieldParser dropper dem). Integritet, ikke sikkerhed -> QC.

## Delopgave 3b (89d8011, koblinger system<->kapabilitet) – kørt 2026-09-24
- BEKRÆFTET lukket: læser PUT med kun capabilityIds, fuld PUT med [], POST
  med capabilityIds, confirm med ekstra capabilityIds-felt -> 403, 0 koblinger
  ændret; anonym 401. Ugyldigt indhold som læser -> 403 (auth før validering);
  JSON-skrald ([null], "abc", [123]) -> 400 for alle (binding før handler,
  som før). Ukendt system-id -> 404 (kendt orakel).
- BEKRÆFTET familie-isolation: PUT rører KUN system.CapabilityLinks (egne).
  Forælder [] lader modulets kobling stå; forælder kan tage samme kapabilitet
  selv; modul [] lader forælderens stå. Flyttes et modul (ParentSystemId),
  følger dets koblinger med og dukker op som HeldBy på den NYE forælder.
  -> Delopgave 4: skift af ParentSystemId skal kræve ret over målforælderen.
- FUND (BEKRÆFTET, robusthed): TOCTOU mellem ValidateCapabilities (uden lås,
  uden for tx) og insert. Import sletter kapabiliteten i vinduet -> FK 23503
  -> ukaldet DbUpdateException -> 500 (Save fanger kun unique). Rigtig race
  import vs PUT: 4/40 og 13/40 x 500. 0 deadlocks (pg_stat_database).
  Samme vindue: blad får børn -> kobling til ikke-blad gemmes (200).
  Rettelse: fang 23503 -> 409, evt. tag ROW EXCLUSIVE på system_capabilities
  i en tx FØR valideringen, så PUT serialiseres med importens LOCK.
- Ressourcer: Distinct før 100-grænsen, O(n). 1M dubletter (admin) -> 200 på
  ~950 ms; 500k distinkte -> 400 på 660 ms; læser 500k -> 403 på 340 ms
  (krop parses før handler – gammelt, gælder alle PUT). Kestrel-loft 30 MB.
- GET /api/systems/{id} med koblinger henter HELE kortet (ToDictionaryAsync):
  5000 kapabiliteter -> 45 ms/kald mod 7 ms uden koblinger; 32 samtidige
  læsere ~50 kald/s. Samme klasse som GET /api/capabilities (43 ms). Ikke nyt
  DoS-niveau, men hent kun de koblede + forfædre, når det skal skaleres.
- Retired[].Systems / AffectedSystems: læser ser de samme navne som via
  ?capabilityId= -> ingen ny læk. Tør-kørsel som læser 403.
- Fingeraftryk: commit genberegner planen under LOCK på begge tabeller og
  sammenligner kun; AffectedSystems (id+navn) indgår -> systemomdøbning
  mellem tør-kørsel og commit giver 409 (ufarligt). Ingen manipulation mulig.

## PoC-mønstre (tilføjet 3b)
- Deterministisk race: åbn Npgsql-forbindelse (cs fra
  EaDbContext.Database.GetConnectionString()), BEGIN, tag importens LOCK,
  start PUT (validering læser frit, insert blokerer), Delay 1,5 s, lav
  DELETE/UPDATE i tx, COMMIT -> se PUT's svar. Deadlock-tjek:
  pg_stat_database.deadlocks for current_database().
- Log via File.AppendAllText i testen; worktree i scratchpad, fjern bagefter.

## PoC-mønstre (tilføjet 3a)
- Angrebstests i egen worktree: `git worktree add --detach <scratch>/wt <sha>`,
  læg fil i tests/Ea.Api.Tests/Zattack/, byg med
  -p:RunAnalyzersDuringBuild=false (analyzere fejler ellers), kør med
  `-- --filter-class ...`. Log til fil (xunit sluger stdout). TestServer-
  HttpClient har 100 s timeout.
- Auth-før-krop: kun Kestrel beviser det (raw socket, stor CL, ingen krop).
- API på Kestrel: Development + ConnectionStrings__Ea=egen DB; token-svar
  hedder `accessToken`. ALDRIG `pkill -f Ea.Api` (rammer egen shell) –
  brug pgrep + kill PID.
- CSV-fuzz: alfabet = + - @ ' , TAB CR LF ; " mellemrum + fuldbredde;
  parse med python csv ',' og TAB; frasortér '-' alene/før mellemrum.

## PoC-mønstre (genbrug)
- CSV-injektion: eksportér, parse med python csv.reader BÅDE delimiter ';'
  og ',', led efter celler der starter med = + - @ (filtrér tomme celler fra).
- Python-hjælper req(m,p,tok,body) med urllib + tokens fra /api/dev/token
  (eva=admin, frida/lars=ingen roller).
- HS256-token i bash: header/payload base64url, openssl dgst -sha256 -hmac KEY.
- Kør API på egen port+DB: ConnectionStrings__Ea + --urls, ryd op med dropdb.
