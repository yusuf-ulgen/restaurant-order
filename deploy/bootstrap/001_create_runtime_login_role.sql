-- ==============================================================================
-- Idempotent Bootstrap Script: Application Runtime Roles & Security Provisioning
-- 
-- Governing Architecture & Execution Order:
-- 1. Separation of Concerns: Privileged role/login provisioning is separated from
--    application schema migrations.
-- 2. Step 1 (DBA / Operator): Execute this script with administrative privileges
--    (e.g., 'postgres') before applying schema migrations:
--      APP_RUNTIME_PASSWORD="<vault_secret>" psql -v ON_ERROR_STOP=1 -f deploy/bootstrap/001_create_runtime_login_role.sql
-- 3. Step 2 (EF Core / CI): Apply schema migrations. The migration checks that
--    'restaurant_app_runtime' exists and grants database-level permissions on schemas/tables.
-- 4. Environment Variables:
--    - APP_RUNTIME_PASSWORD: Password for 'restaurant_app_user'. Extracted via \getenv.
--    - If unset or empty, the script terminates immediately with a fail-fast error.
--    - Never passes secrets via command-line arguments or logs.
-- ==============================================================================

\set ON_ERROR_STOP on

\getenv app_runtime_password APP_RUNTIME_PASSWORD

\if :{?app_runtime_password}
  SELECT CASE
    WHEN length(:'app_runtime_password') = 0 THEN
      'DO $ERR$ BEGIN RAISE EXCEPTION ''Environment variable APP_RUNTIME_PASSWORD must be non-empty.''; END $ERR$;'
    ELSE
      format(
        $BOOTSTRAP$
        DO $BODY$
        DECLARE
            v_pwd text := %L;
        BEGIN
            -- 1. Ensure unprivileged group role exists with strict security attributes
            IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'restaurant_app_runtime') THEN
                CREATE ROLE restaurant_app_runtime WITH NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS;
            ELSE
                ALTER ROLE restaurant_app_runtime WITH NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS;
            END IF;

            -- 2. Ensure application login role exists and update password safely
            IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'restaurant_app_user') THEN
                EXECUTE format('CREATE ROLE restaurant_app_user WITH LOGIN PASSWORD %%L NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS;', v_pwd);
            ELSE
                EXECUTE format('ALTER ROLE restaurant_app_user WITH LOGIN PASSWORD %%L NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS;', v_pwd);
            END IF;

            -- 3. Grant membership in the NOLOGIN runtime group role
            GRANT restaurant_app_runtime TO restaurant_app_user;
        END $BODY$;
        $BOOTSTRAP$,
        :'app_runtime_password'
      )
  END \gexec
\else
  SELECT 'DO $ERR$ BEGIN RAISE EXCEPTION ''Environment variable APP_RUNTIME_PASSWORD is not set. Please set APP_RUNTIME_PASSWORD before executing bootstrap.''; END $ERR$;' \gexec
\endif
