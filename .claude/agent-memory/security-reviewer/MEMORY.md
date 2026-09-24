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

## PoC-mønstre (genbrug)
- HS256-token i bash: header/payload base64url, openssl dgst -sha256 -hmac KEY.
- Kør API på egen port+DB: ConnectionStrings__Ea + --urls, ryd op med dropdb.
