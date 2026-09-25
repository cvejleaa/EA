#!/usr/bin/env bash
# Starter API'et til E2E-testene: Development (dev-login og de FIKTIVE eksempeldata) mod en egen database, ea_e2e,
# der oprettes forfra hver gang — så testene altid starter fra DevSeed og aldrig rører den lokale ea_dev.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
admin="${E2E_PG_ADMIN:-postgresql://ea:ea@localhost:5432/postgres}"

psql "$admin" -v ON_ERROR_STOP=1 -q -c 'DROP DATABASE IF EXISTS ea_e2e WITH (FORCE)'

export ASPNETCORE_ENVIRONMENT=Development
export ConnectionStrings__Ea="${E2E_CONNECTION:-Host=localhost;Database=ea_e2e;Username=ea;Password=ea}"
exec dotnet run --project "$here/../../src/Ea.Api" --no-launch-profile --urls http://localhost:5180
