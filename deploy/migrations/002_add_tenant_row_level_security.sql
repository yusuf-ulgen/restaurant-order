START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260920182029_AddTenantRowLevelSecurity') THEN

                    CREATE OR REPLACE FUNCTION tenancy.get_current_tenant_id() RETURNS uuid STABLE AS $$
                    BEGIN
                        RETURN NULLIF(current_setting('app.current_tenant_id', true), '')::uuid;
                    EXCEPTION WHEN OTHERS THEN
                        RETURN NULL;
                    END;
                    $$ LANGUAGE plpgsql;
                
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260920182029_AddTenantRowLevelSecurity') THEN

                    ALTER TABLE tenancy.tenants ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE tenancy.tenants FORCE ROW LEVEL SECURITY;

                    ALTER TABLE tenancy.brands ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE tenancy.brands FORCE ROW LEVEL SECURITY;

                    ALTER TABLE tenancy.branches ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE tenancy.branches FORCE ROW LEVEL SECURITY;
                
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260920182029_AddTenantRowLevelSecurity') THEN

                    DROP POLICY IF EXISTS tenant_isolation_policy ON tenancy.tenants;
                    CREATE POLICY tenant_isolation_policy ON tenancy.tenants
                        FOR ALL
                        USING (id = tenancy.get_current_tenant_id())
                        WITH CHECK (id = tenancy.get_current_tenant_id());

                    DROP POLICY IF EXISTS brand_isolation_policy ON tenancy.brands;
                    CREATE POLICY brand_isolation_policy ON tenancy.brands
                        FOR ALL
                        USING (tenant_id = tenancy.get_current_tenant_id())
                        WITH CHECK (tenant_id = tenancy.get_current_tenant_id());

                    DROP POLICY IF EXISTS branch_isolation_policy ON tenancy.branches;
                    CREATE POLICY branch_isolation_policy ON tenancy.branches
                        FOR ALL
                        USING (tenant_id = tenancy.get_current_tenant_id())
                        WITH CHECK (tenant_id = tenancy.get_current_tenant_id());
                
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260920182029_AddTenantRowLevelSecurity') THEN

                    DO $CHECK$
                    BEGIN
                        IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'restaurant_app_runtime') THEN
                            RAISE EXCEPTION 'Required group role "restaurant_app_runtime" does not exist. Ensure database bootstrap script (deploy/bootstrap/001_create_runtime_login_role.sql) has been executed by a privileged administrator prior to applying schema migrations.';
                        END IF;
                    END $CHECK$;

                    GRANT USAGE ON SCHEMA tenancy TO restaurant_app_runtime;
                    GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA tenancy TO restaurant_app_runtime;
                    ALTER DEFAULT PRIVILEGES IN SCHEMA tenancy GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO restaurant_app_runtime;
                    REVOKE CREATE ON SCHEMA tenancy FROM restaurant_app_runtime;
                
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260920182029_AddTenantRowLevelSecurity') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260920182029_AddTenantRowLevelSecurity', '10.0.4');
    END IF;
END $EF$;
COMMIT;
