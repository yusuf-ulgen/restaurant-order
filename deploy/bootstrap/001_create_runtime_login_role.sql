-- ==============================================================================
-- Idempotent Bootstrap Script: Application Runtime Login Role Provisioning
-- 
-- Governing Architecture:
-- 1. Separation of Concerns: Privileged role/login provisioning is separated from
--    application schema migrations.
-- 2. Schema migrations define the unprivileged group role 'restaurant_app_runtime'
--    with NOLOGIN, NOSUPERUSER, NOCREATEDB, NOCREATEROLE, NOBYPASSRLS.
-- 3. This bootstrap script is executed by an administrative user (e.g. 'postgres' / DBA)
--    or deployment secret manager to provision the real application LOGIN user.
-- 4. Passwords MUST be cryptographically generated and injected via environment/vault,
--    NEVER committed to source control.
-- ==============================================================================

DO $ROLE$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'restaurant_app_user') THEN
        CREATE ROLE restaurant_app_user WITH LOGIN PASSWORD '${APP_RUNTIME_PASSWORD}'
            NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS;
    ELSE
        ALTER ROLE restaurant_app_user WITH NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS;
    END IF;
END $ROLE$;

-- Grant membership in the NOLOGIN runtime group role (defined in schema migrations)
GRANT restaurant_app_runtime TO restaurant_app_user;
