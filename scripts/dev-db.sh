#!/usr/bin/env bash
# Starter lokal PostgreSQL og opretter udviklings-rollen og -databasen.
# Credentials er FIKTIVE og kun til lokal udvikling/test (samme som i CI).
set -euo pipefail

if command -v pg_lsclusters >/dev/null 2>&1; then
  if pg_lsclusters | awk 'NR>1 {print $4}' | grep -q down; then
    pg_ctlcluster 16 main start
  fi
fi

run_psql() {
  if [ "$(id -u)" = "0" ]; then su postgres -c "psql -v ON_ERROR_STOP=1 -q"; else sudo -u postgres psql -v ON_ERROR_STOP=1 -q; fi
}

run_psql <<'SQL'
DO $$
BEGIN
  IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'ea') THEN
    CREATE ROLE ea LOGIN PASSWORD 'ea' CREATEDB;
  END IF;
END
$$;
SELECT 'CREATE DATABASE ea_dev OWNER ea'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'ea_dev')\gexec
SQL

echo "PostgreSQL klar: Host=localhost;Database=ea_dev;Username=ea;Password=ea"
