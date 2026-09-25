#!/usr/bin/env bash
# Starter API'et til E2E-testene: Development (dev-login og de FIKTIVE eksempeldata) mod en egen database, ea_e2e,
# der oprettes forfra hver gang — så testene altid starter fra DevSeed og aldrig rører den lokale ea_dev. Navnet står
# ét sted: den database, der droppes, er den samme, API'et kører mod.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
db=ea_e2e

psql "postgresql://ea:ea@localhost:5432/postgres" -v ON_ERROR_STOP=1 -q -c "DROP DATABASE IF EXISTS $db WITH (FORCE)"

export ASPNETCORE_ENVIRONMENT=Development
export ConnectionStrings__Ea="Host=localhost;Database=$db;Username=ea;Password=ea"
exec dotnet run --project "$here/../../src/Ea.Api" --no-launch-profile --urls http://localhost:5180
