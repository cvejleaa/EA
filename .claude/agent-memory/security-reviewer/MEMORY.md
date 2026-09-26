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
  SKAL lukkes før delopgave 4 (ikke-admin skriver). -> LUKKET, fuzz-efterprøvet 3d.
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

## Delopgave 3d-server (b8d6ff5, koblingsimport + CsvImport) – kørt 2026-09-24
- BEKRÆFTET lukket: anonym 401, lars/frida 403 på tør-kørsel og commit; raw
  socket CL=100 MB uden krop -> 401/403 på 1 ms (policy før handler, grænsen
  hæves kun i handleren). Admin CL=100 MB -> 400 på 4 ms.
- Integritet BEKRÆFTET: 101 koder -> fejl; 99 + "k9.1"/" K9.2 " -> dublet-fejl
  (NormalizeCode = Trim+ToUpperInvariant, NBSP trimmes også); præcis 100 ok;
  ikke-blad og NY kobling til udgået -> fejl; eksisterende udgået bevares;
  fuldbredde-K -> ukendt kode. Status/team/ejere/beskrivelse/LastConfirmed*
  urørt ved commit (kun UpdatedAt bumpes); andre systemers koblinger md5-ens.
- Races BEKRÆFTET ok: tør->form(koblinger)->commit 409; tør->slet->commit 409;
  import holder lås + sletning i flight -> 200/204; sletning holder RE + import
  -> 204/409; 25 runder stress (2 imports + kortimport + forms + sletninger)
  -> 0 deadlocks, import aldrig 500/halv. Fuld DTU (2000 sys, 10k koblinger):
  commit 1,06 s = låsevindue.
- Formelvagt: 19 håndplukkede + 150 fuzz-navne/beskrivelser (inkl. modul og
  DelesMed " | ") -> 0 formel-celler med ';' ',' TAB. Re-import af eksporten:
  0 fejl, 0 advarsler (round-trip holder). 3a/3b-formelfundene er lukket.
- FUND (BEKRÆFTET, regelbrud indført her): DeleteSystem tager LOCK (RE på
  system_capabilities) FØR adgangstjekket. Læser-DELETE under en import venter
  på låsen (403 efter 4,0 s mod 6 ms) og holder en pool-forbindelse. 15 læser-
  DELETEs mod pool 15 -> eva GET /api/systems hang 19 s (kontrol uden: 7 ms).
  Rettelse: billigt opslag + AuthorizeAsync før tx, derefter lås + genopslag.
- FUND (BEKRÆFTET, admin): 64 MB af "x\n" -> Csv.Read bygger 33M rækker før
  MaxRows-tjek: 5,9 GB peak RSS, 26 s. Rettelse: stop Read ved MaxRows+1.
- FUND (BEKRÆFTET): UpdateSystem loader CapabilityLinks FØR WithCouplingLock.
  Import holder låsen og tilføjer K; form (i kø på låsen) tilføjer også K ->
  23505 pk_system_capabilities -> 500. Form vs sletning -> 23503 -> 500, og
  DeleteSystem SaveChanges -> DbUpdateConcurrencyException -> 500 ved samtidig
  form/confirm (begge gamle). Rettelse: load links efter låsen + map 23505/
  23503/concurrency til 409.
- Kestrel-grænse == egen grænse (64 MB): chunked > 64 MB -> BadHttpRequest ->
  500 i stedet for 400. Sæt Kestrel til MaxFileBytes+1 eller fang undtagelsen.

## PoC-mønstre (tilføjet 3d)
- Hold en rækkelås (SELECT ... FOR UPDATE i psql via subprocess.Popen med
  stdin-pipe og \echo locked) for at stoppe importen EFTER den har taget
  tabel-låsene -> så kan en anden request sættes i kø bag importen. At holde
  importens LOCK selv giver den modsatte rækkefølge (LOCK a, b tages én ad gangen).
- ADVARSEL: PostgreSQL er DELT med andre agenter (max_connections 100). Start
  pool-tests med "Maximum Pool Size=15;Timeout=5" i connection string, ellers
  opbruges serverens slots (53300) for alle. Dræb API'et bagefter (idle pool).
- API fra worktree: cd src/Ea.Api && dotnet bin/Debug/net10.0/Ea.Api.dll
  (content root = cwd, ellers findes appsettings.Development.json ikke).
- RSS: pgrep -f '^dotnet bin/...' (ellers rammes bash-wrapperen).
- Eksporten kan indeholde flerlinjefelter -> byg filer ved at APPENDE rækker
  til eksportens bytes, ikke ved split(';').

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

## Delopgave 4 + Firebase-drift (PLAN-gennemgang, ingen kode) – 2026-09-25
- Firebase-fakta fra auth-emulatorens kilde (firebase-tools master,
  src/emulator/auth/operations.ts; raw.githubusercontent.com er tilgængelig,
  Googles docs-domæner er BLOKERET af proxyen): e-mail-link-login udsteder
  token med sign_in_provider="password" (samme som password-login) og
  identities.email -> serveren kan IKKE skelne link fra password. Link-login
  på eksisterende konto: emailVerified=true, password bevares -> præ-kapring
  virker i emulatoren (produktion: F0 afgør). Uprivilegeret signUp accepterer
  displayName -> "name"-claim er brugerstyret. E-mail-skift -> emailVerified=false.
  EMAIL_SIGNIN-links sendes til enhver adresse (ingen eksistens-tjek).
- Alle Firebase-projekter deler signeringsnøgler: aud/iss er ENESTE binding
  til projektet. Kræv test med token fra "andet projekt".
- BEKRÆFTET (.NET PoC, dotnet run app.cs): tilføjes "name" uden at fjerne
  tokenets, vinder tokenets (FindFirst + Identity.Name). IdentityModel 8
  giver CaseSensitiveClaimsIdentity -> "Roles"/"OID" tæller IKKE som
  roles/oid (kontroltest, lukket).
- BEKRÆFTET: dotnet publish tager appsettings.Development.json (Mode=Dev +
  committet nøgle) med i output -> ASPNETCORE_ENVIRONMENT=Development i en
  container = offentligt dev-login. Krav: udelad filen, K_SERVICE-vagt,
  sikkerheds-smoke (/api/dev/users 404, /api/auth/mode, 401 uden token).
- Ejerens e-mail står i offentlige commit-metadata -> admin-kontoen er
  oplagt præ-kapringsmål (admin bindes på uid).
- 4a-eskalering fundet i design: modul-forvalter kan flytte modul ud af
  fremmed forælder (til top eller egen forælder), hvis MoveSystemRequirement
  kun tjekker system + MÅL. Integrations-PUT har ikke from/to (ingen kapring).
- F4: produktion-data-godkendelse er kun et sikkerhedsnet, hvis deploy-SA
  ikke kan køre/ændre data-jobs, ikke har firebaseauth.*/Editor, og DB-
  brugere er adskilt (app=DML, migrator=DDL, check=read-only).
- PoC-mønster: .NET 10 fil-app: `#:package X@8.*` SKAL stå øverst i filen;
  ClaimsPrincipal.FindFirstValue kræver ASP.NET-ref -> brug FindFirst()?.Value.

## Delopgave 4a (9db34ad, forvaltere redigerer egne systemer) – kørt 2026-09-25
- Model: SystemAccess (scoped, ét opslag pr. request) = roller Systemejer/Systemforvalter
  på systemet + moduler under dem; admin shortcut. Handlerne er scoped. MoveSystem
  kræver system + FRA + TIL (A1 lukket). DeleteSystem = RequireRole(Admin) før lås.
- BEKRÆFTET lukket (HTTP mod testdb, 8 angrebstests): roller på fremmed system/modul
  403 og intet ændret; rolle kun på modul giver ikke forælder (PUT/confirm 403);
  forretningsejer 403; flyt fremmed ind under egen, fremmed modul ud/over, eget modul
  til fremmed forælder -> 403; flyt til ukendt id -> 403 (samme som fremmed, intet
  orakel); forælder+roller+beskrivelse i ét PUT -> 403 og INTET delvist gemt;
  integrationer fremmed<->fremmed 403, PUT/DELETE fremmed 403, from/to/source/target
  i rå JSON ignoreres; canAdd korrekt; admin-only (persons, data-objects, POST systems,
  begge importe inkl. tør-kørsel) 403; under importens lås: forvalter DELETE/PUT-
  fremmed-med-caps/confirm-fremmed -> 403 på 52 ms. Token uden oid / oid "" / " " /
  STORE bogstaver / efterstillet mellemrum -> ingen ret (fail closed). 200 parallelle
  GET Frida/Leo: 0 læk af canEdit mellem brugere (Testing = ingen scope-validering,
  som prod). parent-candidates 3000 systemer: ~33 ms for alle roller.
- LATENT (BEKRÆFTET i DB): person.Oid="" + token oid="" -> ret. Ingen API-vej sætter
  Oid i dag; F1 (Firebase-binding) SKAL afvise tomt/whitespace-claim i SystemAccess
  (IsNullOrWhiteSpace -> tomt sæt) og evt. CHECK (oid <> '').
- ACCEPTERET (BEKRÆFTET): TOCTOU — forvalterens gem, der er godkendt før EA fjerner
  hendes rolle på forælderen, lander stadig (rækkelås på modulet holdt, rolle fjernet,
  lås sluppet -> 200). Vindue = requestets levetid. Historikken (4c) skal vise det.
- Design-note: modul-forvalter kan koble kapabiliteter på modulet, og de vises som
  HeldBy på den fremmede forælder (dækning/overlap ændres). Konsistent med O (modulets
  egne data), men modsat begrundelsen i Q. Ikke fund.
- PoC gemt som mønster: angrebsklasse i Zattack med egen World (P+M, Q+QM, R), Give()
  der BEVARER eksisterende roller, Snap() (version/forælder/roller), token-minting
  med JsonWebTokenHandler + TestUsers.SigningKey for claim-varianter (uden oid osv.).

## F0-script (Firebase-afprøvning, merges ikke) – gennemgået 2026-09-26
- Scriptet ligger i scratchpad/f0/f0.py (ikke committet). Emulator: firebase-tools i samme mappe,
  `npx firebase emulators:start --only auth --project demo-f0` (port 9099). Protokol-id'er nu:
  T0 (nøgle hører til projektet + indstillinger læst via GET v2/projects/{p}/config), T1, T1', T2',
  T3' (præ-kapring via e-mail-skift), T4' (enumeration A1 vs A2: createAuthUri, PASSWORD_RESET,
  forkert password), T5a/a'/f/d/e/bc/h, T6/T6', T7/7'/7p/7", T11 (brugeren sætter selv password),
  T10a/a'/b/c/d. T2/T3/T4 erstattet, T8 -> F1, T9 manuel.
- BEKRÆFTET i emulatoren (også med enableImprovedEmailPrivacy=true): inviteret bruger A sætter
  password (accounts:update {idToken,password}), skifter e-mail til offerets adresse (accounts:update
  {idToken,email} ELLER signUp {idToken,email,password}) -> email_verified=false; ejeren kan så ikke
  oprette offeret (EMAIL_EXISTS); offerets første link-login LANDER I A's uid, email_verified=true,
  A's password virker stadig, A's gamle refresh-token virker stadig. Produktion afgør T6/T6'
  (enumerationsbeskyttelsen skulle blokere updateEmail uden bekræftelse — uverificeret påstand).
- F1-designkrav herfra: bind Person.Oid = fb:uid NÅR ejeren opretter kontoen (uid kendes da),
  ikke via e-mail ved første login; opret Firebase-kontoen samtidig med at personen får e-mail
  i registret; ejer-scriptet skal stoppe hårdt på EMAIL_EXISTS (aldrig "adoptere" en konto).
  VERIFY_AND_CHANGE_EMAIL til offerets adresse (phishing-klik) rammer også e-mail-binding.
- BEKRÆFTET fejl i første udgave: forkert/brugt link ved A2-prompten gav INVALID_OOB_CODE ->
  T5bc "OK" uden at vejen var prøvet (falsk OK på det vigtigste produktionsspørgsmål). Ukaldet
  ApiError i T5d -> traceback, ingen rapport. Løst: link_sign_in spørger igen på BAD_LINK.
- Emulator-fakta: signInWithEmailLink tjekker email == oob.email FØR koden forbruges (INVALID_EMAIL,
  koden kan bruges igen). getProjects returnerer projectNumber som projectId. v2/config findes på
  /identitytoolkit.googleapis.com/v2/projects/{p}/config. Kender ikke disabledUserSignup/-Deletion
  og blokerer ikke updateEmail med privacy slået til.
- PoC-mønster: f0sim.py importerer f0, sætter EMULATOR="" (produktionsvejen) men peger IT/ST på
  emulatoren, wrapper anon() til at afvise som Identity Platform, og builtins.input henter links
  fra /emulator/v1/projects/{p}/oobCodes (også bevidst forkerte links). Kontroltest af dommene.
- Proto3-JSON udelader false-felter: sammenlign med bool(...) når forventningen er False.
