-- Mindste rettighed i driftens database (beslutning X i docs/plan.md).
--
-- UDKAST til F3/F4: køres af ejeren, når databasen på earch findes — aldrig af CI eller af appen, og aldrig mod den
-- lokale ea_dev (lokalt bruges én rolle, `ea`, fordi testene opretter databaser). Efterprøvet mod PostgreSQL 16
-- lokalt af Security Reviewer 2026-09-25 med midlertidige roller:
--   * ea_app kan alle appens operationer, også LOCK TABLE ... ROW EXCLUSIVE og ... SHARE ROW EXCLUSIVE (det kræver
--     UPDATE eller DELETE — ikke TRUNCATE);
--   * ea_app nægtes TRUNCATE, CREATE og DROP (også i public efter REVOKE);
--   * ea_check kan kun læse (nok til at se ventende migreringer i __ef_migrations_history);
--   * en NY tabel, som migratoren opretter, giver kun ea_app SELECT og INSERT — så ændringshistorikken (beslutning W)
--     er append-only af sig selv. UPDATE/DELETE gives eksplicit til de tabeller, der må ændres.
-- Rollerne (ea_migrator, ea_app, ea_check) oprettes med adgangskoder fra Secret Manager, ikke her.

-- Som superuser: psql -v db=<databasens navn> -f scripts/db-roller.sql (første blok), resten som ea_migrator.
REVOKE ALL ON DATABASE :"db" FROM PUBLIC;
GRANT CONNECT ON DATABASE :"db" TO ea_migrator, ea_app, ea_check;
REVOKE ALL ON SCHEMA public FROM PUBLIC;

-- Som ea_migrator, der ejer skemaet ea og kører migreringerne:
GRANT USAGE ON SCHEMA ea TO ea_app, ea_check;
GRANT SELECT ON ALL TABLES IN SCHEMA ea TO ea_check;
GRANT SELECT, INSERT ON ALL TABLES IN SCHEMA ea TO ea_app;
-- De tabeller, appen ændrer og sletter i. En ny tabel, der skal kunne ændres, tilføjes her OG i sin migrering.
GRANT UPDATE, DELETE ON
    ea.systems, ea.system_roles, ea.persons, ea.teams,
    ea.integrations, ea.integration_data_objects, ea.data_objects,
    ea.capabilities, ea.system_capabilities
    TO ea_app;

-- Nye tabeller: kun læse og tilføje (historikken får aldrig UPDATE/DELETE).
ALTER DEFAULT PRIVILEGES FOR ROLE ea_migrator IN SCHEMA ea GRANT SELECT, INSERT ON TABLES TO ea_app;
ALTER DEFAULT PRIVILEGES FOR ROLE ea_migrator IN SCHEMA ea GRANT SELECT ON TABLES TO ea_check;
-- Der er ingen sekvenser i dag (alle nøgler er uuid fra appen); de er med, hvis der kommer nogen.
ALTER DEFAULT PRIVILEGES FOR ROLE ea_migrator IN SCHEMA ea GRANT USAGE, SELECT ON SEQUENCES TO ea_app;
ALTER DEFAULT PRIVILEGES FOR ROLE ea_migrator IN SCHEMA ea GRANT SELECT ON SEQUENCES TO ea_check;
