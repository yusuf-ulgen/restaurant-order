CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260920181655_InitialTenancySchema') THEN
        IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'tenancy') THEN
            CREATE SCHEMA tenancy;
        END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260920181655_InitialTenancySchema') THEN
    CREATE TABLE tenancy.tenants (
        id uuid NOT NULL,
        name character varying(200) NOT NULL,
        slug character varying(64) NOT NULL,
        status character varying(20) NOT NULL,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone,
        concurrency_token uuid NOT NULL,
        CONSTRAINT "PK_tenants" PRIMARY KEY (id),
        CONSTRAINT ck_tenants_status CHECK (status IN ('Active', 'Suspended', 'Closed'))
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260920181655_InitialTenancySchema') THEN
    CREATE TABLE tenancy.brands (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        name character varying(200) NOT NULL,
        slug character varying(64) NOT NULL,
        status character varying(20) NOT NULL,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone,
        concurrency_token uuid NOT NULL,
        CONSTRAINT "PK_brands" PRIMARY KEY (id),
        CONSTRAINT ak_brands_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT ck_brands_status CHECK (status IN ('Active', 'Inactive')),
        CONSTRAINT fk_brands_tenants_tenant_id FOREIGN KEY (tenant_id) REFERENCES tenancy.tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260920181655_InitialTenancySchema') THEN
    CREATE TABLE tenancy.branches (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        brand_id uuid NOT NULL,
        name character varying(200) NOT NULL,
        slug character varying(64) NOT NULL,
        timezone character varying(50) NOT NULL,
        currency character varying(3) NOT NULL,
        status character varying(20) NOT NULL,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone,
        concurrency_token uuid NOT NULL,
        CONSTRAINT "PK_branches" PRIMARY KEY (id),
        CONSTRAINT ck_branches_status CHECK (status IN ('Active', 'Suspended', 'Closed')),
        CONSTRAINT fk_branches_brands_tenant_id_brand_id FOREIGN KEY (tenant_id, brand_id) REFERENCES tenancy.brands (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_branches_tenants_tenant_id FOREIGN KEY (tenant_id) REFERENCES tenancy.tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260920181655_InitialTenancySchema') THEN
    CREATE INDEX ix_branches_tenant_id_brand_id ON tenancy.branches (tenant_id, brand_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260920181655_InitialTenancySchema') THEN
    CREATE UNIQUE INDEX ix_branches_tenant_id_slug ON tenancy.branches (tenant_id, slug);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260920181655_InitialTenancySchema') THEN
    CREATE UNIQUE INDEX ix_brands_tenant_id_slug ON tenancy.brands (tenant_id, slug);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260920181655_InitialTenancySchema') THEN
    CREATE UNIQUE INDEX ix_tenants_slug ON tenancy.tenants (slug);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260920181655_InitialTenancySchema') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260920181655_InitialTenancySchema', '10.0.4');
    END IF;
END $EF$;
COMMIT;

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

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001114508_AddIamPersistenceAndTenantIsolation') THEN
        IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'iam') THEN
            CREATE SCHEMA iam;
        END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001114508_AddIamPersistenceAndTenantIsolation') THEN
    ALTER TABLE tenancy.branches ADD CONSTRAINT ak_branches_tenant_id_id UNIQUE (tenant_id, id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001114508_AddIamPersistenceAndTenantIsolation') THEN
    CREATE TABLE iam.security_audit_events (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        event_type character varying(60) NOT NULL,
        user_id uuid,
        branch_id uuid,
        ip_address character varying(45),
        user_agent character varying(500),
        details_json text,
        created_at_utc timestamp with time zone NOT NULL,
        CONSTRAINT "PK_security_audit_events" PRIMARY KEY (id),
        CONSTRAINT fk_security_audit_events_tenants_tenant_id FOREIGN KEY (tenant_id) REFERENCES tenancy.tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001114508_AddIamPersistenceAndTenantIsolation') THEN
    CREATE TABLE iam.trusted_terminals (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        branch_id uuid NOT NULL,
        device_identifier character varying(100) NOT NULL,
        terminal_name character varying(100) NOT NULL,
        secret_hash character varying(255) NOT NULL,
        is_active boolean NOT NULL DEFAULT TRUE,
        last_heartbeat_at_utc timestamp with time zone,
        enrolled_at_utc timestamp with time zone NOT NULL,
        revoked_at_utc timestamp with time zone,
        CONSTRAINT "PK_trusted_terminals" PRIMARY KEY (id),
        CONSTRAINT fk_trusted_terminals_branches_tenant_id_branch_id FOREIGN KEY (tenant_id, branch_id) REFERENCES tenancy.branches (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_trusted_terminals_tenants_tenant_id FOREIGN KEY (tenant_id) REFERENCES tenancy.tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001114508_AddIamPersistenceAndTenantIsolation') THEN
    CREATE TABLE iam.users (
        id uuid NOT NULL,
        email character varying(255) NOT NULL,
        normalized_email character varying(255) NOT NULL,
        password_hash character varying(500) NOT NULL,
        status integer NOT NULL,
        security_version integer NOT NULL DEFAULT 1,
        failed_login_attempts integer NOT NULL DEFAULT 0,
        lockout_end_utc timestamp with time zone,
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone NOT NULL,
        concurrency_token uuid NOT NULL,
        CONSTRAINT "PK_users" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001114508_AddIamPersistenceAndTenantIsolation') THEN
    CREATE TABLE iam.invitation_tokens (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        user_id uuid NOT NULL,
        token_hash character varying(128) NOT NULL,
        is_consumed boolean NOT NULL DEFAULT FALSE,
        consumed_at_utc timestamp with time zone,
        expires_at_utc timestamp with time zone NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        CONSTRAINT "PK_invitation_tokens" PRIMARY KEY (id),
        CONSTRAINT fk_invitation_tokens_tenants_tenant_id FOREIGN KEY (tenant_id) REFERENCES tenancy.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_invitation_tokens_users_user_id FOREIGN KEY (user_id) REFERENCES iam.users (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001114508_AddIamPersistenceAndTenantIsolation') THEN
    CREATE TABLE iam.memberships (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        user_id uuid NOT NULL,
        role integer NOT NULL,
        branch_id uuid,
        is_active boolean NOT NULL DEFAULT TRUE,
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone NOT NULL,
        concurrency_token uuid NOT NULL,
        CONSTRAINT "PK_memberships" PRIMARY KEY (id),
        CONSTRAINT fk_memberships_branches_tenant_id_branch_id FOREIGN KEY (tenant_id, branch_id) REFERENCES tenancy.branches (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_memberships_tenants_tenant_id FOREIGN KEY (tenant_id) REFERENCES tenancy.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_memberships_users_user_id FOREIGN KEY (user_id) REFERENCES iam.users (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001114508_AddIamPersistenceAndTenantIsolation') THEN
    CREATE TABLE iam.password_reset_tokens (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        user_id uuid NOT NULL,
        token_hash character varying(128) NOT NULL,
        is_consumed boolean NOT NULL DEFAULT FALSE,
        consumed_at_utc timestamp with time zone,
        expires_at_utc timestamp with time zone NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        CONSTRAINT "PK_password_reset_tokens" PRIMARY KEY (id),
        CONSTRAINT fk_password_reset_tokens_tenants_tenant_id FOREIGN KEY (tenant_id) REFERENCES tenancy.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_password_reset_tokens_users_user_id FOREIGN KEY (user_id) REFERENCES iam.users (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001114508_AddIamPersistenceAndTenantIsolation') THEN
    CREATE TABLE iam.pin_credentials (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        user_id uuid NOT NULL,
        branch_id uuid NOT NULL,
        pin_hash character varying(500) NOT NULL,
        algorithm_version character varying(50) NOT NULL,
        pepper_key_id character varying(50) NOT NULL,
        failed_pin_attempts integer NOT NULL DEFAULT 0,
        locked_until_utc timestamp with time zone,
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone NOT NULL,
        CONSTRAINT "PK_pin_credentials" PRIMARY KEY (id),
        CONSTRAINT fk_pin_credentials_branches_tenant_id_branch_id FOREIGN KEY (tenant_id, branch_id) REFERENCES tenancy.branches (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_pin_credentials_tenants_tenant_id FOREIGN KEY (tenant_id) REFERENCES tenancy.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_pin_credentials_users_user_id FOREIGN KEY (user_id) REFERENCES iam.users (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001114508_AddIamPersistenceAndTenantIsolation') THEN
    CREATE TABLE iam.sessions (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        user_id uuid NOT NULL,
        membership_id uuid NOT NULL,
        branch_id uuid,
        auth_method integer NOT NULL,
        ip_address character varying(45),
        user_agent character varying(500),
        is_revoked boolean NOT NULL DEFAULT FALSE,
        revoked_at_utc timestamp with time zone,
        revocation_reason character varying(100),
        created_at_utc timestamp with time zone NOT NULL,
        last_seen_at_utc timestamp with time zone NOT NULL,
        expires_at_utc timestamp with time zone NOT NULL,
        CONSTRAINT "PK_sessions" PRIMARY KEY (id),
        CONSTRAINT fk_sessions_memberships_membership_id FOREIGN KEY (membership_id) REFERENCES iam.memberships (id) ON DELETE CASCADE,
        CONSTRAINT fk_sessions_tenants_tenant_id FOREIGN KEY (tenant_id) REFERENCES tenancy.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_sessions_users_user_id FOREIGN KEY (user_id) REFERENCES iam.users (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001114508_AddIamPersistenceAndTenantIsolation') THEN
    CREATE TABLE iam.refresh_tokens (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        session_id uuid NOT NULL,
        token_family_id uuid NOT NULL,
        token_hash character varying(128) NOT NULL,
        is_revoked boolean NOT NULL DEFAULT FALSE,
        revoked_at_utc timestamp with time zone,
        replaced_by_token_id uuid,
        created_at_utc timestamp with time zone NOT NULL,
        expires_at_utc timestamp with time zone NOT NULL,
        CONSTRAINT "PK_refresh_tokens" PRIMARY KEY (id),
        CONSTRAINT fk_refresh_tokens_sessions_session_id FOREIGN KEY (session_id) REFERENCES iam.sessions (id) ON DELETE CASCADE,
        CONSTRAINT fk_refresh_tokens_tenants_tenant_id FOREIGN KEY (tenant_id) REFERENCES tenancy.tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001114508_AddIamPersistenceAndTenantIsolation') THEN
    CREATE INDEX "IX_invitation_tokens_user_id" ON iam.invitation_tokens (user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001114508_AddIamPersistenceAndTenantIsolation') THEN
    CREATE INDEX ix_invitation_tokens_tenant_id ON iam.invitation_tokens (tenant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001114508_AddIamPersistenceAndTenantIsolation') THEN
    CREATE UNIQUE INDEX ix_invitation_tokens_token_hash ON iam.invitation_tokens (token_hash);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001114508_AddIamPersistenceAndTenantIsolation') THEN
    CREATE INDEX ix_memberships_tenant_branch ON iam.memberships (tenant_id, branch_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001114508_AddIamPersistenceAndTenantIsolation') THEN
    CREATE INDEX ix_memberships_tenant_id ON iam.memberships (tenant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001114508_AddIamPersistenceAndTenantIsolation') THEN
    CREATE INDEX ix_memberships_user_id ON iam.memberships (user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001114508_AddIamPersistenceAndTenantIsolation') THEN
    CREATE INDEX "IX_password_reset_tokens_user_id" ON iam.password_reset_tokens (user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001114508_AddIamPersistenceAndTenantIsolation') THEN
    CREATE INDEX ix_password_reset_tokens_tenant_id ON iam.password_reset_tokens (tenant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001114508_AddIamPersistenceAndTenantIsolation') THEN
    CREATE UNIQUE INDEX ix_password_reset_tokens_token_hash ON iam.password_reset_tokens (token_hash);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001114508_AddIamPersistenceAndTenantIsolation') THEN
    CREATE INDEX "IX_pin_credentials_tenant_id_branch_id" ON iam.pin_credentials (tenant_id, branch_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001114508_AddIamPersistenceAndTenantIsolation') THEN
    CREATE INDEX "IX_pin_credentials_user_id" ON iam.pin_credentials (user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001114508_AddIamPersistenceAndTenantIsolation') THEN
    CREATE UNIQUE INDEX ix_pin_credentials_tenant_user_branch ON iam.pin_credentials (tenant_id, user_id, branch_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001114508_AddIamPersistenceAndTenantIsolation') THEN
    CREATE INDEX ix_refresh_tokens_session_id ON iam.refresh_tokens (session_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001114508_AddIamPersistenceAndTenantIsolation') THEN
    CREATE INDEX ix_refresh_tokens_tenant_id ON iam.refresh_tokens (tenant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001114508_AddIamPersistenceAndTenantIsolation') THEN
    CREATE INDEX ix_refresh_tokens_token_family_id ON iam.refresh_tokens (token_family_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001114508_AddIamPersistenceAndTenantIsolation') THEN
    CREATE UNIQUE INDEX ix_refresh_tokens_token_hash ON iam.refresh_tokens (token_hash);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001114508_AddIamPersistenceAndTenantIsolation') THEN
    CREATE INDEX ix_security_audit_events_created_at_utc ON iam.security_audit_events (created_at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001114508_AddIamPersistenceAndTenantIsolation') THEN
    CREATE INDEX ix_security_audit_events_event_type ON iam.security_audit_events (event_type);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001114508_AddIamPersistenceAndTenantIsolation') THEN
    CREATE INDEX ix_security_audit_events_tenant_id ON iam.security_audit_events (tenant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001114508_AddIamPersistenceAndTenantIsolation') THEN
    CREATE INDEX ix_security_audit_events_user_id ON iam.security_audit_events (user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001114508_AddIamPersistenceAndTenantIsolation') THEN
    CREATE INDEX ix_sessions_expires_at_utc ON iam.sessions (expires_at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001114508_AddIamPersistenceAndTenantIsolation') THEN
    CREATE INDEX ix_sessions_membership_id ON iam.sessions (membership_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001114508_AddIamPersistenceAndTenantIsolation') THEN
    CREATE INDEX ix_sessions_tenant_id ON iam.sessions (tenant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001114508_AddIamPersistenceAndTenantIsolation') THEN
    CREATE INDEX ix_sessions_user_id ON iam.sessions (user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001114508_AddIamPersistenceAndTenantIsolation') THEN
    CREATE UNIQUE INDEX ix_trusted_terminals_tenant_branch_device ON iam.trusted_terminals (tenant_id, branch_id, device_identifier);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001114508_AddIamPersistenceAndTenantIsolation') THEN
    CREATE UNIQUE INDEX ix_users_normalized_email ON iam.users (normalized_email);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001114508_AddIamPersistenceAndTenantIsolation') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261001114508_AddIamPersistenceAndTenantIsolation', '10.0.4');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001114628_AddIamRowLevelSecurity') THEN

                    DO $CHECK$
                    BEGIN
                        IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'restaurant_app_runtime') THEN
                            RAISE EXCEPTION 'Required group role "restaurant_app_runtime" does not exist. Ensure database bootstrap script (deploy/bootstrap/001_create_runtime_login_role.sql) has been executed by a privileged administrator prior to applying schema migrations.';
                        END IF;
                    END $CHECK$;

    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001114628_AddIamRowLevelSecurity') THEN

                    ALTER TABLE iam.memberships ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE iam.memberships FORCE ROW LEVEL SECURITY;

                    ALTER TABLE iam.sessions ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE iam.sessions FORCE ROW LEVEL SECURITY;

                    ALTER TABLE iam.refresh_tokens ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE iam.refresh_tokens FORCE ROW LEVEL SECURITY;

                    ALTER TABLE iam.pin_credentials ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE iam.pin_credentials FORCE ROW LEVEL SECURITY;

                    ALTER TABLE iam.trusted_terminals ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE iam.trusted_terminals FORCE ROW LEVEL SECURITY;

                    ALTER TABLE iam.security_audit_events ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE iam.security_audit_events FORCE ROW LEVEL SECURITY;

                    ALTER TABLE iam.invitation_tokens ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE iam.invitation_tokens FORCE ROW LEVEL SECURITY;

                    ALTER TABLE iam.password_reset_tokens ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE iam.password_reset_tokens FORCE ROW LEVEL SECURITY;

    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001114628_AddIamRowLevelSecurity') THEN

                    DROP POLICY IF EXISTS membership_isolation_policy ON iam.memberships;
                    CREATE POLICY membership_isolation_policy ON iam.memberships
                        FOR ALL
                        USING (tenant_id = tenancy.get_current_tenant_id())
                        WITH CHECK (tenant_id = tenancy.get_current_tenant_id());

                    DROP POLICY IF EXISTS session_isolation_policy ON iam.sessions;
                    CREATE POLICY session_isolation_policy ON iam.sessions
                        FOR ALL
                        USING (tenant_id = tenancy.get_current_tenant_id())
                        WITH CHECK (tenant_id = tenancy.get_current_tenant_id());

                    DROP POLICY IF EXISTS refresh_token_isolation_policy ON iam.refresh_tokens;
                    CREATE POLICY refresh_token_isolation_policy ON iam.refresh_tokens
                        FOR ALL
                        USING (tenant_id = tenancy.get_current_tenant_id())
                        WITH CHECK (tenant_id = tenancy.get_current_tenant_id());

                    DROP POLICY IF EXISTS pin_credential_isolation_policy ON iam.pin_credentials;
                    CREATE POLICY pin_credential_isolation_policy ON iam.pin_credentials
                        FOR ALL
                        USING (tenant_id = tenancy.get_current_tenant_id())
                        WITH CHECK (tenant_id = tenancy.get_current_tenant_id());

                    DROP POLICY IF EXISTS trusted_terminal_isolation_policy ON iam.trusted_terminals;
                    CREATE POLICY trusted_terminal_isolation_policy ON iam.trusted_terminals
                        FOR ALL
                        USING (tenant_id = tenancy.get_current_tenant_id())
                        WITH CHECK (tenant_id = tenancy.get_current_tenant_id());

                    DROP POLICY IF EXISTS security_audit_events_isolation_policy ON iam.security_audit_events;
                    CREATE POLICY security_audit_events_isolation_policy ON iam.security_audit_events
                        FOR ALL
                        USING (tenant_id = tenancy.get_current_tenant_id())
                        WITH CHECK (tenant_id = tenancy.get_current_tenant_id());

                    DROP POLICY IF EXISTS invitation_tokens_isolation_policy ON iam.invitation_tokens;
                    CREATE POLICY invitation_tokens_isolation_policy ON iam.invitation_tokens
                        FOR ALL
                        USING (tenant_id = tenancy.get_current_tenant_id())
                        WITH CHECK (tenant_id = tenancy.get_current_tenant_id());

                    DROP POLICY IF EXISTS password_reset_tokens_isolation_policy ON iam.password_reset_tokens;
                    CREATE POLICY password_reset_tokens_isolation_policy ON iam.password_reset_tokens
                        FOR ALL
                        USING (tenant_id = tenancy.get_current_tenant_id())
                        WITH CHECK (tenant_id = tenancy.get_current_tenant_id());

    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001114628_AddIamRowLevelSecurity') THEN

                    CREATE OR REPLACE FUNCTION iam.prevent_audit_tampering() RETURNS trigger AS $$
                    BEGIN
                        RAISE EXCEPTION 'Security audit log rows are append-only. UPDATE and DELETE operations are strictly prohibited.';
                    END;
                    $$ LANGUAGE plpgsql;

                    DROP TRIGGER IF EXISTS trg_prevent_audit_tampering ON iam.security_audit_events;
                    CREATE TRIGGER trg_prevent_audit_tampering
                        BEFORE UPDATE OR DELETE ON iam.security_audit_events
                        FOR EACH ROW EXECUTE FUNCTION iam.prevent_audit_tampering();

    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001114628_AddIamRowLevelSecurity') THEN

                    CREATE OR REPLACE FUNCTION iam.lookup_user_for_login(p_normalized_email text)
                    RETURNS TABLE (
                        user_id uuid,
                        normalized_email text,
                        password_hash text,
                        status integer,
                        security_version integer,
                        lockout_end_utc timestamp with time zone
                    )
                    LANGUAGE plpgsql
                    SECURITY DEFINER
                    SET search_path = iam, pg_temp
                    AS $$
                    BEGIN
                        RETURN QUERY
                        SELECT
                            u.id,
                            u.normalized_email::text,
                            u.password_hash::text,
                            u.status,
                            u.security_version,
                            u.lockout_end_utc
                        FROM iam.users u
                        WHERE u.normalized_email = p_normalized_email
                        LIMIT 1;
                    END;
                    $$;

    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001114628_AddIamRowLevelSecurity') THEN

                    GRANT USAGE ON SCHEMA iam TO restaurant_app_runtime;
                    REVOKE CREATE ON SCHEMA iam FROM restaurant_app_runtime;

                    -- On global users: revoke wide SELECT; grant minimal column access and execution of lookup function
                    REVOKE ALL ON iam.users FROM restaurant_app_runtime;
                    GRANT INSERT ON iam.users TO restaurant_app_runtime;
                    GRANT UPDATE (password_hash, status, security_version, failed_login_attempts, lockout_end_utc, updated_at_utc, concurrency_token) ON iam.users TO restaurant_app_runtime;
                    GRANT SELECT (id, status, security_version, created_at_utc, updated_at_utc, concurrency_token) ON iam.users TO restaurant_app_runtime;
                    GRANT EXECUTE ON FUNCTION iam.lookup_user_for_login(text) TO restaurant_app_runtime;

                    -- On tenant-owned IAM tables: DML access governed strictly by RLS
                    GRANT SELECT, INSERT, UPDATE, DELETE ON
                        iam.memberships,
                        iam.sessions,
                        iam.refresh_tokens,
                        iam.pin_credentials,
                        iam.trusted_terminals,
                        iam.invitation_tokens,
                        iam.password_reset_tokens
                    TO restaurant_app_runtime;

                    -- On security audit events: append-only (SELECT, INSERT allowed; UPDATE and DELETE strictly forbidden)
                    GRANT SELECT, INSERT ON iam.security_audit_events TO restaurant_app_runtime;
                    REVOKE UPDATE, DELETE ON iam.security_audit_events FROM restaurant_app_runtime;

    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001114628_AddIamRowLevelSecurity') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261001114628_AddIamRowLevelSecurity', '10.0.4');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001140000_AddIamPreAuthBootstrapFunctionsAndTransactions') THEN

                    CREATE OR REPLACE FUNCTION iam.lookup_tenant_by_slug(p_slug text)
                    RETURNS TABLE (
                        tenant_id uuid,
                        name text,
                        slug text,
                        status text
                    )
                    LANGUAGE plpgsql
                    SECURITY DEFINER
                    SET search_path = tenancy, pg_temp
                    AS $$
                    BEGIN
                        RETURN QUERY
                        SELECT t.id, t.name::text, t.slug::text, t.status::text
                        FROM tenancy.tenants t
                        WHERE t.slug = p_slug
                        LIMIT 1;
                    END;
                    $$;

                    REVOKE ALL ON FUNCTION iam.lookup_tenant_by_slug(text) FROM PUBLIC;
                    GRANT EXECUTE ON FUNCTION iam.lookup_tenant_by_slug(text) TO restaurant_app_runtime;

    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001140000_AddIamPreAuthBootstrapFunctionsAndTransactions') THEN

                    CREATE OR REPLACE FUNCTION iam.lookup_membership_for_login(p_tenant_id uuid, p_user_id uuid)
                    RETURNS TABLE (
                        membership_id uuid,
                        tenant_id uuid,
                        user_id uuid,
                        role text,
                        branch_id uuid,
                        is_active boolean
                    )
                    LANGUAGE plpgsql
                    SECURITY DEFINER
                    SET search_path = iam, pg_temp
                    AS $$
                    BEGIN
                        RETURN QUERY
                        SELECT m.id, m.tenant_id, m.user_id, m.role::text, m.branch_id, m.is_active
                        FROM iam.memberships m
                        WHERE m.tenant_id = p_tenant_id
                          AND m.user_id = p_user_id
                          AND m.is_active = true
                        LIMIT 1;
                    END;
                    $$;

                    REVOKE ALL ON FUNCTION iam.lookup_membership_for_login(uuid, uuid) FROM PUBLIC;
                    GRANT EXECUTE ON FUNCTION iam.lookup_membership_for_login(uuid, uuid) TO restaurant_app_runtime;

    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001140000_AddIamPreAuthBootstrapFunctionsAndTransactions') THEN

                    CREATE OR REPLACE FUNCTION iam.lookup_invitation_token(p_token_hash text)
                    RETURNS TABLE (
                        invitation_id uuid,
                        tenant_id uuid,
                        user_id uuid,
                        expires_at_utc timestamp with time zone,
                        is_consumed boolean
                    )
                    LANGUAGE plpgsql
                    SECURITY DEFINER
                    SET search_path = iam, pg_temp
                    AS $$
                    BEGIN
                        RETURN QUERY
                        SELECT i.id, i.tenant_id, i.user_id, i.expires_at_utc, i.is_consumed
                        FROM iam.invitation_tokens i
                        WHERE i.token_hash = p_token_hash
                        LIMIT 1;
                    END;
                    $$;

                    REVOKE ALL ON FUNCTION iam.lookup_invitation_token(text) FROM PUBLIC;
                    GRANT EXECUTE ON FUNCTION iam.lookup_invitation_token(text) TO restaurant_app_runtime;

    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001140000_AddIamPreAuthBootstrapFunctionsAndTransactions') THEN

                    CREATE OR REPLACE FUNCTION iam.lookup_password_reset_token(p_token_hash text)
                    RETURNS TABLE (
                        reset_token_id uuid,
                        tenant_id uuid,
                        user_id uuid,
                        expires_at_utc timestamp with time zone,
                        is_consumed boolean
                    )
                    LANGUAGE plpgsql
                    SECURITY DEFINER
                    SET search_path = iam, pg_temp
                    AS $$
                    BEGIN
                        RETURN QUERY
                        SELECT p.id, p.tenant_id, p.user_id, p.expires_at_utc, p.is_consumed
                        FROM iam.password_reset_tokens p
                        WHERE p.token_hash = p_token_hash
                        LIMIT 1;
                    END;
                    $$;

                    REVOKE ALL ON FUNCTION iam.lookup_password_reset_token(text) FROM PUBLIC;
                    GRANT EXECUTE ON FUNCTION iam.lookup_password_reset_token(text) TO restaurant_app_runtime;

    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001140000_AddIamPreAuthBootstrapFunctionsAndTransactions') THEN

                    CREATE OR REPLACE FUNCTION iam.lookup_terminal_for_auth(p_terminal_id uuid)
                    RETURNS TABLE (
                        terminal_id uuid,
                        tenant_id uuid,
                        branch_id uuid,
                        device_identifier text,
                        terminal_name text,
                        secret_hash text,
                        is_active boolean
                    )
                    LANGUAGE plpgsql
                    SECURITY DEFINER
                    SET search_path = iam, pg_temp
                    AS $$
                    BEGIN
                        RETURN QUERY
                        SELECT t.id, t.tenant_id, t.branch_id, t.device_identifier::text, t.terminal_name::text, t.secret_hash::text, t.is_active
                        FROM iam.trusted_terminals t
                        WHERE t.id = p_terminal_id
                        LIMIT 1;
                    END;
                    $$;

                    REVOKE ALL ON FUNCTION iam.lookup_terminal_for_auth(uuid) FROM PUBLIC;
                    GRANT EXECUTE ON FUNCTION iam.lookup_terminal_for_auth(uuid) TO restaurant_app_runtime;

    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001140000_AddIamPreAuthBootstrapFunctionsAndTransactions') THEN

                    CREATE OR REPLACE FUNCTION iam.lookup_first_active_membership_by_user(p_user_id uuid)
                    RETURNS TABLE (
                        membership_id uuid,
                        tenant_id uuid,
                        branch_id uuid,
                        role text
                    )
                    LANGUAGE plpgsql
                    SECURITY DEFINER
                    SET search_path = iam, pg_temp
                    AS $$
                    BEGIN
                        RETURN QUERY
                        SELECT m.id, m.tenant_id, m.branch_id, m.role::text
                        FROM iam.memberships m
                        WHERE m.user_id = p_user_id AND m.is_active = true
                        LIMIT 1;
                    END;
                    $$;

                    REVOKE ALL ON FUNCTION iam.lookup_first_active_membership_by_user(uuid) FROM PUBLIC;
                    GRANT EXECUTE ON FUNCTION iam.lookup_first_active_membership_by_user(uuid) TO restaurant_app_runtime;

    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001140000_AddIamPreAuthBootstrapFunctionsAndTransactions') THEN

                    CREATE OR REPLACE FUNCTION iam.lookup_user_by_id(p_user_id uuid)
                    RETURNS TABLE (
                        user_id uuid,
                        email text,
                        normalized_email text,
                        status integer,
                        security_version integer,
                        lockout_end_utc timestamp with time zone
                    )
                    LANGUAGE plpgsql
                    SECURITY DEFINER
                    SET search_path = iam, pg_temp
                    AS $$
                    BEGIN
                        RETURN QUERY
                        SELECT u.id, u.email::text, u.normalized_email::text, u.status, u.security_version, u.lockout_end_utc
                        FROM iam.users u
                        WHERE u.id = p_user_id
                        LIMIT 1;
                    END;
                    $$;

                    REVOKE ALL ON FUNCTION iam.lookup_user_by_id(uuid) FROM PUBLIC;
                    GRANT EXECUTE ON FUNCTION iam.lookup_user_by_id(uuid) TO restaurant_app_runtime;

    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001140000_AddIamPreAuthBootstrapFunctionsAndTransactions') THEN

                    CREATE OR REPLACE FUNCTION iam.lookup_session_tenant(p_session_id uuid)
                    RETURNS TABLE (
                        session_id uuid,
                        tenant_id uuid
                    )
                    LANGUAGE plpgsql
                    SECURITY DEFINER
                    SET search_path = iam, pg_temp
                    AS $$
                    BEGIN
                        RETURN QUERY
                        SELECT s.id, s.tenant_id
                        FROM iam.sessions s
                        WHERE s.id = p_session_id
                        LIMIT 1;
                    END;
                    $$;

                    REVOKE ALL ON FUNCTION iam.lookup_session_tenant(uuid) FROM PUBLIC;
                    GRANT EXECUTE ON FUNCTION iam.lookup_session_tenant(uuid) TO restaurant_app_runtime;

    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001140000_AddIamPreAuthBootstrapFunctionsAndTransactions') THEN

                    REVOKE ALL ON FUNCTION iam.lookup_user_for_login(text) FROM PUBLIC;
                    GRANT EXECUTE ON FUNCTION iam.lookup_user_for_login(text) TO restaurant_app_runtime;

    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001140000_AddIamPreAuthBootstrapFunctionsAndTransactions') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261001140000_AddIamPreAuthBootstrapFunctionsAndTransactions', '10.0.4');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001160000_AddDistributedAuthStateAndPlatformSessions') THEN

                    CREATE TABLE IF NOT EXISTS iam.platform_sessions (
                        id uuid NOT NULL,
                        user_id uuid NOT NULL,
                        family_id uuid NOT NULL,
                        current_token_hash character varying(64) NOT NULL,
                        created_at_utc timestamp with time zone NOT NULL,
                        last_seen_at_utc timestamp with time zone NOT NULL,
                        expires_at_utc timestamp with time zone NOT NULL,
                        is_revoked boolean NOT NULL DEFAULT false,
                        revoked_at_utc timestamp with time zone,
                        revocation_reason character varying(100),
                        ip_address character varying(45),
                        user_agent character varying(500),
                        concurrency_token character varying(36) NOT NULL,
                        CONSTRAINT pk_platform_sessions PRIMARY KEY (id),
                        CONSTRAINT fk_platform_sessions_users_user_id FOREIGN KEY (user_id) REFERENCES iam.users(id) ON DELETE CASCADE
                    );

                    CREATE INDEX IF NOT EXISTS ix_platform_sessions_user_id ON iam.platform_sessions(user_id);
                    CREATE INDEX IF NOT EXISTS ix_platform_sessions_expires_at_utc ON iam.platform_sessions(expires_at_utc);

    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001160000_AddDistributedAuthStateAndPlatformSessions') THEN

                    CREATE TABLE IF NOT EXISTS iam.platform_refresh_tokens (
                        id uuid NOT NULL,
                        session_id uuid NOT NULL,
                        token_family_id uuid NOT NULL,
                        token_hash character varying(64) NOT NULL,
                        replaced_by_token_id uuid,
                        created_at_utc timestamp with time zone NOT NULL,
                        expires_at_utc timestamp with time zone NOT NULL,
                        is_revoked boolean NOT NULL DEFAULT false,
                        revoked_at_utc timestamp with time zone,
                        CONSTRAINT pk_platform_refresh_tokens PRIMARY KEY (id),
                        CONSTRAINT fk_platform_refresh_tokens_session_id FOREIGN KEY (session_id) REFERENCES iam.platform_sessions(id) ON DELETE CASCADE,
                        CONSTRAINT uq_platform_refresh_tokens_hash UNIQUE (token_hash)
                    );

                    CREATE INDEX IF NOT EXISTS ix_platform_refresh_tokens_session_id ON iam.platform_refresh_tokens(session_id);
                    CREATE INDEX IF NOT EXISTS ix_platform_refresh_tokens_family_id ON iam.platform_refresh_tokens(token_family_id);

    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001160000_AddDistributedAuthStateAndPlatformSessions') THEN

                    GRANT SELECT, INSERT, UPDATE, DELETE ON iam.platform_sessions TO restaurant_app_runtime;
                    GRANT SELECT, INSERT, UPDATE, DELETE ON iam.platform_refresh_tokens TO restaurant_app_runtime;
                    GRANT REFERENCES ON iam.users TO restaurant_app_runtime;
                    GRANT SELECT (failed_login_attempts, lockout_end_utc) ON iam.users TO restaurant_app_runtime;

    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001160000_AddDistributedAuthStateAndPlatformSessions') THEN

                    DROP FUNCTION IF EXISTS iam.lookup_tenant_by_slug(text);
                    CREATE OR REPLACE FUNCTION iam.lookup_tenant_by_slug(p_slug text)
                    RETURNS TABLE (
                        tenant_id uuid,
                        name text,
                        slug text,
                        status text
                    )
                    LANGUAGE plpgsql
                    SECURITY DEFINER
                    SET search_path = tenancy, pg_temp
                    AS $$
                    BEGIN
                        RETURN QUERY
                        SELECT t.id, t.name::text, t.slug::text, t.status::text
                        FROM tenancy.tenants t
                        WHERE t.slug = p_slug
                        LIMIT 1;
                    END;
                    $$;

                    REVOKE ALL ON FUNCTION iam.lookup_tenant_by_slug(text) FROM PUBLIC;
                    GRANT EXECUTE ON FUNCTION iam.lookup_tenant_by_slug(text) TO restaurant_app_runtime;

    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001160000_AddDistributedAuthStateAndPlatformSessions') THEN

                    CREATE OR REPLACE FUNCTION iam.lookup_refresh_token_for_rotation(p_token_hash text)
                    RETURNS TABLE (
                        token_id uuid,
                        tenant_id uuid,
                        session_id uuid,
                        token_family_id uuid,
                        expires_at_utc timestamp with time zone,
                        is_revoked boolean,
                        replaced_by_token_id uuid,
                        user_id uuid,
                        auth_method integer,
                        session_is_revoked boolean,
                        session_expires_at_utc timestamp with time zone
                    )
                    LANGUAGE plpgsql
                    SECURITY DEFINER
                    SET search_path = iam, pg_temp
                    AS $$
                    BEGIN
                        RETURN QUERY
                        SELECT rt.id, rt.tenant_id, rt.session_id, rt.token_family_id, rt.expires_at_utc, rt.is_revoked, rt.replaced_by_token_id,
                               s.user_id, s.auth_method, s.is_revoked, s.expires_at_utc
                        FROM iam.refresh_tokens rt
                        JOIN iam.sessions s ON s.id = rt.session_id
                        WHERE rt.token_hash = p_token_hash
                        LIMIT 1;
                    END;
                    $$;

                    REVOKE ALL ON FUNCTION iam.lookup_refresh_token_for_rotation(text) FROM PUBLIC;
                    GRANT EXECUTE ON FUNCTION iam.lookup_refresh_token_for_rotation(text) TO restaurant_app_runtime;

    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001160000_AddDistributedAuthStateAndPlatformSessions') THEN

                    CREATE OR REPLACE FUNCTION iam.validate_token_session(
                        p_session_id uuid,
                        p_user_id uuid,
                        p_security_version integer
                    )
                    RETURNS TABLE (
                        is_valid boolean,
                        failure_reason text
                    )
                    LANGUAGE plpgsql
                    SECURITY DEFINER
                    SET search_path = iam, pg_temp
                    AS $$
                    DECLARE
                        v_user_status integer;
                        v_user_sec_ver integer;
                        v_session_revoked boolean;
                        v_session_expires timestamptz;
                    BEGIN
                        -- Validate user account exists, is active, and security_version matches
                        SELECT u.status, u.security_version
                        INTO v_user_status, v_user_sec_ver
                        FROM iam.users u
                        WHERE u.id = p_user_id;

                        IF NOT FOUND THEN
                            RETURN QUERY SELECT false, 'user_not_found'::text;
                            RETURN;
                        END IF;

                        IF v_user_status <> 2 THEN -- UserStatus.Active = 2
                            RETURN QUERY SELECT false, 'user_inactive'::text;
                            RETURN;
                        END IF;

                        IF v_user_sec_ver <> p_security_version THEN
                            RETURN QUERY SELECT false, 'security_version_mismatch'::text;
                            RETURN;
                        END IF;

                        -- Check in tenant sessions
                        SELECT s.is_revoked, s.expires_at_utc
                        INTO v_session_revoked, v_session_expires
                        FROM iam.sessions s
                        WHERE s.id = p_session_id;

                        IF FOUND THEN
                            IF v_session_revoked OR v_session_expires <= now() THEN
                                RETURN QUERY SELECT false, 'session_revoked_or_expired'::text;
                                RETURN;
                            END IF;
                            RETURN QUERY SELECT true, 'valid'::text;
                            RETURN;
                        END IF;

                        -- Check in platform sessions (for SuperAdmin)
                        SELECT ps.is_revoked, ps.expires_at_utc
                        INTO v_session_revoked, v_session_expires
                        FROM iam.platform_sessions ps
                        WHERE ps.id = p_session_id;

                        IF FOUND THEN
                            IF v_session_revoked OR v_session_expires <= now() THEN
                                RETURN QUERY SELECT false, 'platform_session_revoked_or_expired'::text;
                                RETURN;
                            END IF;
                            RETURN QUERY SELECT true, 'valid'::text;
                            RETURN;
                        END IF;

                        RETURN QUERY SELECT false, 'session_not_found'::text;
                    END;
                    $$;

                    REVOKE ALL ON FUNCTION iam.validate_token_session(uuid, uuid, integer) FROM PUBLIC;
                    GRANT EXECUTE ON FUNCTION iam.validate_token_session(uuid, uuid, integer) TO restaurant_app_runtime;

    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001160000_AddDistributedAuthStateAndPlatformSessions') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261001160000_AddDistributedAuthStateAndPlatformSessions', '10.0.4');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001180000_HardenPlatformSessionsAndAtomicLockout') THEN

                    REVOKE SELECT, INSERT, UPDATE, DELETE ON iam.platform_sessions FROM restaurant_app_runtime;
                    REVOKE SELECT, INSERT, UPDATE, DELETE ON iam.platform_refresh_tokens FROM restaurant_app_runtime;

    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001180000_HardenPlatformSessionsAndAtomicLockout') THEN

                    CREATE OR REPLACE FUNCTION iam.validate_token_session(
                        p_session_id uuid, p_user_id uuid, p_security_version integer
                    ) RETURNS TABLE (is_valid boolean, failure_reason text)
                    LANGUAGE plpgsql SECURITY DEFINER SET search_path = iam, pg_temp AS $$
                    DECLARE
                        v_user_status integer; v_user_sec_ver integer;
                        v_session_revoked boolean; v_session_expires timestamptz; v_session_user_id uuid;
                    BEGIN
                        SELECT u.status, u.security_version INTO v_user_status, v_user_sec_ver FROM iam.users u WHERE u.id = p_user_id;
                        IF NOT FOUND THEN RETURN QUERY SELECT false, 'user_not_found'::text; RETURN; END IF;
                        IF v_user_status <> 2 THEN RETURN QUERY SELECT false, 'user_inactive'::text; RETURN; END IF;
                        IF v_user_sec_ver <> p_security_version THEN RETURN QUERY SELECT false, 'security_version_mismatch'::text; RETURN; END IF;

                        -- Check in tenant sessions (strictly enforcing s.user_id = p_user_id)
                        SELECT s.user_id, s.is_revoked, s.expires_at_utc INTO v_session_user_id, v_session_revoked, v_session_expires
                        FROM iam.sessions s WHERE s.id = p_session_id;
                        IF FOUND THEN
                            IF v_session_user_id <> p_user_id THEN RETURN QUERY SELECT false, 'session_user_mismatch'::text; RETURN; END IF;
                            IF v_session_revoked OR v_session_expires <= now() THEN RETURN QUERY SELECT false, 'session_revoked_or_expired'::text; RETURN; END IF;
                            RETURN QUERY SELECT true, 'valid'::text; RETURN;
                        END IF;

                        -- Check in platform sessions (strictly enforcing ps.user_id = p_user_id)
                        SELECT ps.user_id, ps.is_revoked, ps.expires_at_utc INTO v_session_user_id, v_session_revoked, v_session_expires
                        FROM iam.platform_sessions ps WHERE ps.id = p_session_id;
                        IF FOUND THEN
                            IF v_session_user_id <> p_user_id THEN RETURN QUERY SELECT false, 'session_user_mismatch'::text; RETURN; END IF;
                            IF v_session_revoked OR v_session_expires <= now() THEN RETURN QUERY SELECT false, 'platform_session_revoked_or_expired'::text; RETURN; END IF;
                            RETURN QUERY SELECT true, 'valid'::text; RETURN;
                        END IF;

                        RETURN QUERY SELECT false, 'session_not_found'::text;
                    END; $$;
                    REVOKE ALL ON FUNCTION iam.validate_token_session(uuid, uuid, integer) FROM PUBLIC;
                    GRANT EXECUTE ON FUNCTION iam.validate_token_session(uuid, uuid, integer) TO restaurant_app_runtime;

    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001180000_HardenPlatformSessionsAndAtomicLockout') THEN

                    CREATE OR REPLACE FUNCTION iam.record_failed_login_attempt(
                        p_user_id uuid, p_max_attempts integer, p_lockout_minutes integer, p_now_utc timestamptz
                    ) RETURNS TABLE (new_failed_attempts integer, new_status integer, new_lockout_end timestamptz)
                    LANGUAGE plpgsql SECURITY DEFINER SET search_path = iam, pg_temp AS $$
                    BEGIN
                        RETURN QUERY
                        UPDATE iam.users
                        SET failed_login_attempts = failed_login_attempts + 1,
                            status = CASE WHEN (failed_login_attempts + 1) >= p_max_attempts THEN 3 ELSE status END,
                            lockout_end_utc = CASE WHEN (failed_login_attempts + 1) >= p_max_attempts THEN p_now_utc + (p_lockout_minutes * INTERVAL '1 minute') ELSE lockout_end_utc END,
                            updated_at_utc = p_now_utc,
                            concurrency_token = gen_random_uuid()
                        WHERE id = p_user_id
                        RETURNING failed_login_attempts, status, lockout_end_utc;
                    END; $$;
                    REVOKE ALL ON FUNCTION iam.record_failed_login_attempt(uuid, integer, integer, timestamptz) FROM PUBLIC;
                    GRANT EXECUTE ON FUNCTION iam.record_failed_login_attempt(uuid, integer, integer, timestamptz) TO restaurant_app_runtime;

    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001180000_HardenPlatformSessionsAndAtomicLockout') THEN

                    -- Create platform session & initial refresh token
                    CREATE OR REPLACE FUNCTION iam.create_platform_session(
                        p_session_id uuid, p_user_id uuid, p_family_id uuid, p_token_id uuid,
                        p_token_hash text, p_now_utc timestamptz, p_session_expires timestamptz,
                        p_token_expires timestamptz, p_ip text, p_user_agent text
                    ) RETURNS void LANGUAGE plpgsql SECURITY DEFINER SET search_path = iam, pg_temp AS $$
                    BEGIN
                        INSERT INTO iam.platform_sessions (
                            id, user_id, family_id, current_token_hash, created_at_utc, last_seen_at_utc, expires_at_utc, is_revoked, concurrency_token, ip_address, user_agent
                        ) VALUES (
                            p_session_id, p_user_id, p_family_id, p_token_hash, p_now_utc, p_now_utc, p_session_expires, false, gen_random_uuid()::text, p_ip, p_user_agent
                        );
                        INSERT INTO iam.platform_refresh_tokens (
                            id, session_id, token_family_id, token_hash, expires_at_utc, created_at_utc, is_revoked
                        ) VALUES (
                            p_token_id, p_session_id, p_family_id, p_token_hash, p_token_expires, p_now_utc, false
                        );
                    END; $$;
                    REVOKE ALL ON FUNCTION iam.create_platform_session(uuid, uuid, uuid, uuid, text, timestamptz, timestamptz, timestamptz, text, text) FROM PUBLIC;
                    GRANT EXECUTE ON FUNCTION iam.create_platform_session(uuid, uuid, uuid, uuid, text, timestamptz, timestamptz, timestamptz, text, text) TO restaurant_app_runtime;

                    -- Rotate platform refresh token with FOR UPDATE and reuse detection
                    CREATE OR REPLACE FUNCTION iam.rotate_platform_refresh_token(
                        p_old_token_hash text, p_new_token_id uuid, p_new_token_hash text,
                        p_now_utc timestamptz, p_new_token_expires timestamptz
                    ) RETURNS TABLE (success boolean, is_reuse boolean, out_session_id uuid, out_user_id uuid, out_family_id uuid)
                    LANGUAGE plpgsql SECURITY DEFINER SET search_path = iam, pg_temp AS $$
                    DECLARE
                        v_token_id uuid; v_session_id uuid; v_family_id uuid;
                        v_token_revoked boolean; v_replaced_by uuid; v_token_expires timestamptz;
                        v_user_id uuid; v_session_revoked boolean; v_session_expires timestamptz;
                    BEGIN
                        SELECT id, session_id, token_family_id, is_revoked, replaced_by_token_id, expires_at_utc
                        INTO v_token_id, v_session_id, v_family_id, v_token_revoked, v_replaced_by, v_token_expires
                        FROM iam.platform_refresh_tokens WHERE token_hash = p_old_token_hash FOR UPDATE;
                        IF NOT FOUND THEN RETURN QUERY SELECT false, false, NULL::uuid, NULL::uuid, NULL::uuid; RETURN; END IF;

                        SELECT user_id, is_revoked, expires_at_utc INTO v_user_id, v_session_revoked, v_session_expires
                        FROM iam.platform_sessions WHERE id = v_session_id FOR UPDATE;
                        IF NOT FOUND THEN RETURN QUERY SELECT false, false, NULL::uuid, NULL::uuid, NULL::uuid; RETURN; END IF;

                        -- Reuse detected
                        IF v_token_revoked OR v_replaced_by IS NOT NULL THEN
                            UPDATE iam.platform_refresh_tokens SET is_revoked = true, revoked_at_utc = p_now_utc WHERE token_family_id = v_family_id;
                            UPDATE iam.platform_sessions SET is_revoked = true, revocation_reason = 'token_reuse_detected', revoked_at_utc = p_now_utc WHERE id = v_session_id;
                            RETURN QUERY SELECT false, true, v_session_id, v_user_id, v_family_id; RETURN; END IF;

                        IF v_token_expires <= p_now_utc OR v_session_revoked OR v_session_expires <= p_now_utc THEN
                            RETURN QUERY SELECT false, false, v_session_id, v_user_id, v_family_id; RETURN; END IF;

                        UPDATE iam.platform_refresh_tokens SET is_revoked = true, replaced_by_token_id = p_new_token_id, revoked_at_utc = p_now_utc WHERE id = v_token_id;
                        INSERT INTO iam.platform_refresh_tokens (id, session_id, token_family_id, token_hash, expires_at_utc, created_at_utc, is_revoked)
                        VALUES (p_new_token_id, v_session_id, v_family_id, p_new_token_hash, p_new_token_expires, p_now_utc, false);
                        UPDATE iam.platform_sessions SET current_token_hash = p_new_token_hash, last_seen_at_utc = p_now_utc, concurrency_token = gen_random_uuid()::text WHERE id = v_session_id;
                        RETURN QUERY SELECT true, false, v_session_id, v_user_id, v_family_id;
                    END; $$;
                    REVOKE ALL ON FUNCTION iam.rotate_platform_refresh_token(text, uuid, text, timestamptz, timestamptz) FROM PUBLIC;
                    GRANT EXECUTE ON FUNCTION iam.rotate_platform_refresh_token(text, uuid, text, timestamptz, timestamptz) TO restaurant_app_runtime;

                    -- Revoke platform session strictly verifying user ownership (Anti-IDOR)
                    CREATE OR REPLACE FUNCTION iam.revoke_platform_session(
                        p_user_id uuid, p_session_id uuid, p_reason text, p_now_utc timestamptz
                    ) RETURNS boolean LANGUAGE plpgsql SECURITY DEFINER SET search_path = iam, pg_temp AS $$
                    DECLARE v_rows integer;
                    BEGIN
                        UPDATE iam.platform_sessions SET is_revoked = true, revocation_reason = p_reason, revoked_at_utc = p_now_utc
                        WHERE id = p_session_id AND (p_user_id IS NULL OR user_id = p_user_id) AND is_revoked = false;
                        GET DIAGNOSTICS v_rows = ROW_COUNT;
                        IF v_rows > 0 THEN
                            UPDATE iam.platform_refresh_tokens SET is_revoked = true, revoked_at_utc = p_now_utc
                            WHERE session_id = p_session_id AND is_revoked = false;
                            RETURN true;
                        END IF;
                        RETURN EXISTS (SELECT 1 FROM iam.platform_sessions WHERE id = p_session_id AND (p_user_id IS NULL OR user_id = p_user_id));
                    END; $$;
                    REVOKE ALL ON FUNCTION iam.revoke_platform_session(uuid, uuid, text, timestamptz) FROM PUBLIC;
                    GRANT EXECUTE ON FUNCTION iam.revoke_platform_session(uuid, uuid, text, timestamptz) TO restaurant_app_runtime;

                    -- Revoke all platform sessions for a user
                    CREATE OR REPLACE FUNCTION iam.revoke_all_user_platform_sessions(
                        p_user_id uuid, p_now_utc timestamptz
                    ) RETURNS void LANGUAGE plpgsql SECURITY DEFINER SET search_path = iam, pg_temp AS $$
                    BEGIN
                        UPDATE iam.platform_sessions SET is_revoked = true, revocation_reason = 'revoke_all', revoked_at_utc = p_now_utc
                        WHERE user_id = p_user_id AND is_revoked = false;
                        UPDATE iam.platform_refresh_tokens rt SET is_revoked = true, revoked_at_utc = p_now_utc
                        FROM iam.platform_sessions ps WHERE rt.session_id = ps.id AND ps.user_id = p_user_id AND rt.is_revoked = false;
                    END; $$;
                    REVOKE ALL ON FUNCTION iam.revoke_all_user_platform_sessions(uuid, timestamptz) FROM PUBLIC;
                    GRANT EXECUTE ON FUNCTION iam.revoke_all_user_platform_sessions(uuid, timestamptz) TO restaurant_app_runtime;

                    -- List active platform sessions for user
                    CREATE OR REPLACE FUNCTION iam.list_active_platform_sessions(
                        p_user_id uuid, p_now_utc timestamptz
                    ) RETURNS TABLE (
                        id uuid, user_id uuid, family_id uuid, current_token_hash text,
                        created_at_utc timestamptz, last_seen_at_utc timestamptz, expires_at_utc timestamptz, is_revoked boolean
                    ) LANGUAGE plpgsql SECURITY DEFINER SET search_path = iam, pg_temp AS $$
                    BEGIN
                        RETURN QUERY SELECT ps.id, ps.user_id, ps.family_id, ps.current_token_hash::text,
                               ps.created_at_utc, ps.last_seen_at_utc, ps.expires_at_utc, ps.is_revoked
                        FROM iam.platform_sessions ps
                        WHERE ps.user_id = p_user_id AND ps.is_revoked = false AND ps.expires_at_utc > p_now_utc
                        ORDER BY ps.last_seen_at_utc DESC;
                    END; $$;
                    REVOKE ALL ON FUNCTION iam.list_active_platform_sessions(uuid, timestamptz) FROM PUBLIC;
                    GRANT EXECUTE ON FUNCTION iam.list_active_platform_sessions(uuid, timestamptz) TO restaurant_app_runtime;

                    -- Active check for platform session
                    CREATE OR REPLACE FUNCTION iam.is_platform_session_active(
                        p_session_id uuid, p_now_utc timestamptz
                    ) RETURNS boolean LANGUAGE plpgsql SECURITY DEFINER SET search_path = iam, pg_temp AS $$
                    BEGIN
                        RETURN EXISTS (
                            SELECT 1 FROM iam.platform_sessions
                            WHERE id = p_session_id AND is_revoked = false AND expires_at_utc > p_now_utc
                        );
                    END; $$;
                    REVOKE ALL ON FUNCTION iam.is_platform_session_active(uuid, timestamptz) FROM PUBLIC;
                    GRANT EXECUTE ON FUNCTION iam.is_platform_session_active(uuid, timestamptz) TO restaurant_app_runtime;

    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001180000_HardenPlatformSessionsAndAtomicLockout') THEN

                    CREATE OR REPLACE FUNCTION iam.lookup_session_tenant_for_user(
                        p_session_id uuid, p_user_id uuid
                    ) RETURNS TABLE (session_id uuid, tenant_id uuid)
                    LANGUAGE plpgsql SECURITY DEFINER SET search_path = iam, pg_temp AS $$
                    BEGIN
                        RETURN QUERY SELECT s.id, s.tenant_id FROM iam.sessions s
                        WHERE s.id = p_session_id AND (p_user_id IS NULL OR s.user_id = p_user_id) LIMIT 1;
                    END; $$;
                    REVOKE ALL ON FUNCTION iam.lookup_session_tenant_for_user(uuid, uuid) FROM PUBLIC;
                    GRANT EXECUTE ON FUNCTION iam.lookup_session_tenant_for_user(uuid, uuid) TO restaurant_app_runtime;

    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001180000_HardenPlatformSessionsAndAtomicLockout') THEN

                    CREATE OR REPLACE FUNCTION iam.revoke_tenant_session_for_user(
                        p_tenant_id uuid, p_user_id uuid, p_session_id uuid, p_reason text, p_now_utc timestamptz
                    ) RETURNS boolean LANGUAGE plpgsql SECURITY DEFINER SET search_path = iam, pg_temp AS $$
                    DECLARE v_rows integer;
                    BEGIN
                        UPDATE iam.sessions SET is_revoked = true, revocation_reason = p_reason, revoked_at_utc = p_now_utc
                        WHERE id = p_session_id AND tenant_id = p_tenant_id AND (p_user_id IS NULL OR user_id = p_user_id) AND is_revoked = false;
                        GET DIAGNOSTICS v_rows = ROW_COUNT;
                        IF v_rows > 0 THEN
                            UPDATE iam.refresh_tokens SET is_revoked = true, revoked_at_utc = p_now_utc
                            WHERE session_id = p_session_id AND tenant_id = p_tenant_id AND is_revoked = false;
                            RETURN true;
                        END IF;
                        RETURN EXISTS (SELECT 1 FROM iam.sessions WHERE id = p_session_id AND tenant_id = p_tenant_id AND (p_user_id IS NULL OR user_id = p_user_id));
                    END; $$;
                    REVOKE ALL ON FUNCTION iam.revoke_tenant_session_for_user(uuid, uuid, uuid, text, timestamptz) FROM PUBLIC;
                    GRANT EXECUTE ON FUNCTION iam.revoke_tenant_session_for_user(uuid, uuid, uuid, text, timestamptz) TO restaurant_app_runtime;

    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001180000_HardenPlatformSessionsAndAtomicLockout') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261001180000_HardenPlatformSessionsAndAtomicLockout', '10.0.4');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001200000_AddUserMembershipStatusAndAtomicTokens') THEN
    ALTER TABLE iam.memberships ADD status integer NOT NULL DEFAULT 1;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001200000_AddUserMembershipStatusAndAtomicTokens') THEN

                    UPDATE iam.memberships
                    SET status = CASE WHEN is_active = true THEN 1 ELSE 3 END
                    WHERE status = 0 OR status IS NULL;

    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001200000_AddUserMembershipStatusAndAtomicTokens') THEN

                    CREATE OR REPLACE FUNCTION iam.lookup_active_memberships_by_user(p_user_id uuid)
                    RETURNS TABLE (
                        membership_id uuid,
                        tenant_id uuid,
                        branch_id uuid,
                        role text,
                        status integer
                    )
                    LANGUAGE plpgsql
                    SECURITY DEFINER
                    SET search_path = iam, pg_temp
                    AS $$
                    BEGIN
                        RETURN QUERY
                        SELECT m.id, m.tenant_id, m.branch_id, m.role::text, m.status
                        FROM iam.memberships m
                        WHERE m.user_id = p_user_id
                          AND m.status = 1
                          AND m.is_active = true;
                    END;
                    $$;

                    REVOKE ALL ON FUNCTION iam.lookup_active_memberships_by_user(uuid) FROM PUBLIC;
                    GRANT EXECUTE ON FUNCTION iam.lookup_active_memberships_by_user(uuid) TO restaurant_app_runtime;

    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001200000_AddUserMembershipStatusAndAtomicTokens') THEN

                    CREATE OR REPLACE FUNCTION iam.consume_invitation_token(p_token_hash text, p_now_utc timestamp with time zone)
                    RETURNS TABLE (
                        invitation_id uuid,
                        tenant_id uuid,
                        user_id uuid
                    )
                    LANGUAGE plpgsql
                    SECURITY DEFINER
                    SET search_path = iam, pg_temp
                    AS $$
                    BEGIN
                        RETURN QUERY
                        UPDATE iam.invitation_tokens t
                        SET is_consumed = TRUE,
                            consumed_at_utc = p_now_utc
                        WHERE t.token_hash = p_token_hash
                          AND t.is_consumed = FALSE
                          AND t.expires_at_utc > p_now_utc
                        RETURNING t.id, t.tenant_id, t.user_id;
                    END;
                    $$;

                    REVOKE ALL ON FUNCTION iam.consume_invitation_token(text, timestamp with time zone) FROM PUBLIC;
                    GRANT EXECUTE ON FUNCTION iam.consume_invitation_token(text, timestamp with time zone) TO restaurant_app_runtime;

    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001200000_AddUserMembershipStatusAndAtomicTokens') THEN

                    CREATE OR REPLACE FUNCTION iam.consume_password_reset_token(p_token_hash text, p_now_utc timestamp with time zone)
                    RETURNS TABLE (
                        reset_token_id uuid,
                        tenant_id uuid,
                        user_id uuid
                    )
                    LANGUAGE plpgsql
                    SECURITY DEFINER
                    SET search_path = iam, pg_temp
                    AS $$
                    BEGIN
                        RETURN QUERY
                        UPDATE iam.password_reset_tokens t
                        SET is_consumed = TRUE,
                            consumed_at_utc = p_now_utc
                        WHERE t.token_hash = p_token_hash
                          AND t.is_consumed = FALSE
                          AND t.expires_at_utc > p_now_utc
                        RETURNING t.id, t.tenant_id, t.user_id;
                    END;
                    $$;

                    REVOKE ALL ON FUNCTION iam.consume_password_reset_token(text, timestamp with time zone) FROM PUBLIC;
                    GRANT EXECUTE ON FUNCTION iam.consume_password_reset_token(text, timestamp with time zone) TO restaurant_app_runtime;

    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001200000_AddUserMembershipStatusAndAtomicTokens') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261001200000_AddUserMembershipStatusAndAtomicTokens', '10.0.4');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001220000_AddIdentityNotificationOutboxAndScopedRevocation') THEN
    CREATE TABLE iam.identity_notifications_outbox (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        notification_type character varying(50) NOT NULL,
        recipient_email character varying(255) NOT NULL,
        encrypted_payload text NOT NULL,
        idempotency_key character varying(128) NOT NULL,
        status integer NOT NULL DEFAULT 1,
        attempt_count integer NOT NULL DEFAULT 0,
        max_attempts integer NOT NULL DEFAULT 5,
        next_attempt_utc timestamp with time zone NOT NULL,
        last_error text,
        claim_token uuid,
        claimed_at_utc timestamp with time zone,
        locked_until_utc timestamp with time zone,
        delivered_at_utc timestamp with time zone,
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone NOT NULL,
        CONSTRAINT pk_identity_notifications_outbox PRIMARY KEY (id),
        CONSTRAINT fk_identity_notifications_outbox_tenants_tenant_id FOREIGN KEY (tenant_id) REFERENCES tenancy.tenants (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001220000_AddIdentityNotificationOutboxAndScopedRevocation') THEN
    CREATE INDEX ix_identity_notifications_outbox_tenant_id ON iam.identity_notifications_outbox (tenant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001220000_AddIdentityNotificationOutboxAndScopedRevocation') THEN
    CREATE UNIQUE INDEX uq_identity_notifications_outbox_idempotency_key ON iam.identity_notifications_outbox (idempotency_key);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001220000_AddIdentityNotificationOutboxAndScopedRevocation') THEN
    CREATE INDEX ix_identity_notifications_outbox_status_next_attempt ON iam.identity_notifications_outbox (status, next_attempt_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001220000_AddIdentityNotificationOutboxAndScopedRevocation') THEN
    CREATE INDEX ix_identity_notifications_outbox_status_locked_until ON iam.identity_notifications_outbox (status, locked_until_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001220000_AddIdentityNotificationOutboxAndScopedRevocation') THEN

                    ALTER TABLE iam.identity_notifications_outbox ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE iam.identity_notifications_outbox FORCE ROW LEVEL SECURITY;

                    DROP POLICY IF EXISTS identity_notifications_outbox_isolation_policy ON iam.identity_notifications_outbox;
                    CREATE POLICY identity_notifications_outbox_isolation_policy ON iam.identity_notifications_outbox
                        FOR ALL
                        USING (tenant_id = tenancy.get_current_tenant_id())
                        WITH CHECK (tenant_id = tenancy.get_current_tenant_id());

                    GRANT SELECT, INSERT, UPDATE, DELETE ON iam.identity_notifications_outbox TO restaurant_app_runtime;

    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001220000_AddIdentityNotificationOutboxAndScopedRevocation') THEN

                    CREATE OR REPLACE FUNCTION iam.claim_pending_identity_notifications(
                        p_limit integer,
                        p_now_utc timestamp with time zone,
                        p_lease_seconds integer,
                        p_claim_token uuid
                    )
                    RETURNS TABLE (
                        id uuid,
                        tenant_id uuid,
                        notification_type text,
                        recipient_email text,
                        encrypted_payload text,
                        idempotency_key text,
                        attempt_count integer,
                        max_attempts integer,
                        claim_token uuid
                    )
                    LANGUAGE plpgsql
                    SECURITY DEFINER
                    SET search_path = iam, pg_temp
                    AS $$
                    BEGIN
                        RETURN QUERY
                        WITH cte AS (
                            SELECT o.id
                            FROM iam.identity_notifications_outbox o
                            WHERE (o.status = 1 AND o.next_attempt_utc <= p_now_utc)
                               OR (o.status = 2 AND o.locked_until_utc < p_now_utc)
                            ORDER BY o.next_attempt_utc ASC
                            LIMIT p_limit
                            FOR UPDATE SKIP LOCKED
                        )
                        UPDATE iam.identity_notifications_outbox o
                        SET status = 2,
                            claim_token = p_claim_token,
                            claimed_at_utc = p_now_utc,
                            locked_until_utc = p_now_utc + (p_lease_seconds || ' seconds')::interval,
                            attempt_count = o.attempt_count + 1,
                            updated_at_utc = p_now_utc
                        FROM cte
                        WHERE o.id = cte.id
                        RETURNING o.id, o.tenant_id, o.notification_type::text, o.recipient_email::text, o.encrypted_payload::text, o.idempotency_key::text, o.attempt_count, o.max_attempts, o.claim_token;
                    END;
                    $$;

                    REVOKE ALL ON FUNCTION iam.claim_pending_identity_notifications(integer, timestamp with time zone, integer, uuid) FROM PUBLIC;
                    GRANT EXECUTE ON FUNCTION iam.claim_pending_identity_notifications(integer, timestamp with time zone, integer, uuid) TO restaurant_app_runtime;

    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001220000_AddIdentityNotificationOutboxAndScopedRevocation') THEN

                    CREATE OR REPLACE FUNCTION iam.complete_identity_notification(
                        p_id uuid,
                        p_claim_token uuid,
                        p_now_utc timestamp with time zone
                    )
                    RETURNS boolean
                    LANGUAGE plpgsql
                    SECURITY DEFINER
                    SET search_path = iam, pg_temp
                    AS $$
                    DECLARE
                        v_rows integer;
                    BEGIN
                        UPDATE iam.identity_notifications_outbox
                        SET status = 3,
                            claim_token = NULL,
                            locked_until_utc = NULL,
                            delivered_at_utc = p_now_utc,
                            updated_at_utc = p_now_utc
                        WHERE id = p_id
                          AND claim_token = p_claim_token
                          AND status = 2;

                        GET DIAGNOSTICS v_rows = ROW_COUNT;
                        RETURN v_rows > 0;
                    END;
                    $$;

                    REVOKE ALL ON FUNCTION iam.complete_identity_notification(uuid, uuid, timestamp with time zone) FROM PUBLIC;
                    GRANT EXECUTE ON FUNCTION iam.complete_identity_notification(uuid, uuid, timestamp with time zone) TO restaurant_app_runtime;

                    CREATE OR REPLACE FUNCTION iam.fail_identity_notification(
                        p_id uuid,
                        p_claim_token uuid,
                        p_error text,
                        p_next_attempt_utc timestamp with time zone,
                        p_is_dead_letter boolean,
                        p_now_utc timestamp with time zone
                    )
                    RETURNS boolean
                    LANGUAGE plpgsql
                    SECURITY DEFINER
                    SET search_path = iam, pg_temp
                    AS $$
                    DECLARE
                        v_rows integer;
                    BEGIN
                        UPDATE iam.identity_notifications_outbox
                        SET status = CASE WHEN p_is_dead_letter THEN 4 ELSE 1 END,
                            claim_token = NULL,
                            locked_until_utc = NULL,
                            last_error = substring(p_error from 1 for 500),
                            next_attempt_utc = p_next_attempt_utc,
                            updated_at_utc = p_now_utc
                        WHERE id = p_id
                          AND claim_token = p_claim_token
                          AND status = 2;

                        GET DIAGNOSTICS v_rows = ROW_COUNT;
                        RETURN v_rows > 0;
                    END;
                    $$;

                    REVOKE ALL ON FUNCTION iam.fail_identity_notification(uuid, uuid, text, timestamp with time zone, boolean, timestamp with time zone) FROM PUBLIC;
                    GRANT EXECUTE ON FUNCTION iam.fail_identity_notification(uuid, uuid, text, timestamp with time zone, boolean, timestamp with time zone) TO restaurant_app_runtime;

    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001220000_AddIdentityNotificationOutboxAndScopedRevocation') THEN

                    CREATE OR REPLACE FUNCTION iam.revoke_membership_sessions(
                        p_user_id uuid,
                        p_tenant_id uuid,
                        p_branch_id uuid,
                        p_now_utc timestamp with time zone
                    )
                    RETURNS TABLE (session_id uuid)
                    LANGUAGE plpgsql
                    SECURITY DEFINER
                    SET search_path = iam, pg_temp
                    AS $$
                    BEGIN
                        -- 1. Revoke matching refresh tokens
                        UPDATE iam.refresh_tokens rt
                        SET is_revoked = true,
                            revoked_at_utc = p_now_utc
                        FROM iam.sessions s
                        WHERE rt.session_id = s.id
                          AND s.user_id = p_user_id
                          AND s.tenant_id = p_tenant_id
                          AND (p_branch_id IS NULL OR s.branch_id = p_branch_id)
                          AND rt.is_revoked = false;

                        -- 2. Revoke matching sessions and return their IDs
                        RETURN QUERY
                        UPDATE iam.sessions s
                        SET is_revoked = true,
                            revocation_reason = 'membership_status_changed',
                            revoked_at_utc = p_now_utc
                        WHERE s.user_id = p_user_id
                          AND s.tenant_id = p_tenant_id
                          AND (p_branch_id IS NULL OR s.branch_id = p_branch_id)
                          AND s.is_revoked = false
                        RETURNING s.id;
                    END;
                    $$;

                    REVOKE ALL ON FUNCTION iam.revoke_membership_sessions(uuid, uuid, uuid, timestamp with time zone) FROM PUBLIC;
                    GRANT EXECUTE ON FUNCTION iam.revoke_membership_sessions(uuid, uuid, uuid, timestamp with time zone) TO restaurant_app_runtime;

    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001220000_AddIdentityNotificationOutboxAndScopedRevocation') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261001220000_AddIdentityNotificationOutboxAndScopedRevocation', '10.0.4');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261002010000_ClearClaimedAtUtcInOutboxCompletion') THEN

                    CREATE OR REPLACE FUNCTION iam.complete_identity_notification(
                        p_id uuid,
                        p_claim_token uuid,
                        p_now_utc timestamp with time zone
                    )
                    RETURNS boolean
                    LANGUAGE plpgsql
                    SECURITY DEFINER
                    SET search_path = iam, pg_temp
                    AS $$
                    DECLARE
                        v_rows integer;
                    BEGIN
                        UPDATE iam.identity_notifications_outbox
                        SET status = 3,
                            claim_token = NULL,
                            claimed_at_utc = NULL,
                            locked_until_utc = NULL,
                            delivered_at_utc = p_now_utc,
                            updated_at_utc = p_now_utc
                        WHERE id = p_id
                          AND claim_token = p_claim_token
                          AND status = 2;

                        GET DIAGNOSTICS v_rows = ROW_COUNT;
                        RETURN v_rows > 0;
                    END;
                    $$;

                    CREATE OR REPLACE FUNCTION iam.fail_identity_notification(
                        p_id uuid,
                        p_claim_token uuid,
                        p_error text,
                        p_next_attempt_utc timestamp with time zone,
                        p_is_dead_letter boolean,
                        p_now_utc timestamp with time zone
                    )
                    RETURNS boolean
                    LANGUAGE plpgsql
                    SECURITY DEFINER
                    SET search_path = iam, pg_temp
                    AS $$
                    DECLARE
                        v_rows integer;
                    BEGIN
                        UPDATE iam.identity_notifications_outbox
                        SET status = CASE WHEN p_is_dead_letter THEN 4 ELSE 1 END,
                            claim_token = NULL,
                            claimed_at_utc = NULL,
                            locked_until_utc = NULL,
                            last_error = substring(p_error from 1 for 500),
                            next_attempt_utc = p_next_attempt_utc,
                            updated_at_utc = p_now_utc
                        WHERE id = p_id
                          AND claim_token = p_claim_token
                          AND status = 2;

                        GET DIAGNOSTICS v_rows = ROW_COUNT;
                        RETURN v_rows > 0;
                    END;
                    $$;

    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261002010000_ClearClaimedAtUtcInOutboxCompletion') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261002010000_ClearClaimedAtUtcInOutboxCompletion', '10.0.4');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261002100000_AddBrandAppearanceAndBranchThemeOverrides') THEN
    CREATE TABLE tenancy.brand_appearances (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        brand_id uuid NOT NULL,
        display_name character varying(200) NOT NULL,
        logo_url character varying(500),
        favicon_url character varying(500),
        primary_color character varying(7) NOT NULL,
        primary_hover_color character varying(7) NOT NULL,
        secondary_color character varying(7) NOT NULL,
        accent_color character varying(7) NOT NULL,
        surface_color character varying(7) NOT NULL,
        background_color character varying(7) NOT NULL,
        footer_text character varying(500),
        default_shell_title character varying(150),
        default_shell_subtitle character varying(250),
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone,
        concurrency_token uuid NOT NULL,
        CONSTRAINT "PK_brand_appearances" PRIMARY KEY (id),
        CONSTRAINT fk_brand_appearances_brands_tenant_id_brand_id FOREIGN KEY (tenant_id, brand_id) REFERENCES tenancy.brands (tenant_id, id) ON DELETE CASCADE,
        CONSTRAINT fk_brand_appearances_tenants_tenant_id FOREIGN KEY (tenant_id) REFERENCES tenancy.tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261002100000_AddBrandAppearanceAndBranchThemeOverrides') THEN
    CREATE TABLE tenancy.branch_theme_overrides (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        branch_id uuid NOT NULL,
        display_name character varying(200),
        logo_url character varying(500),
        header_subtitle character varying(250),
        footer_branch_info character varying(500),
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone,
        concurrency_token uuid NOT NULL,
        CONSTRAINT "PK_branch_theme_overrides" PRIMARY KEY (id),
        CONSTRAINT fk_branch_theme_overrides_branches_tenant_id_branch_id FOREIGN KEY (tenant_id, branch_id) REFERENCES tenancy.branches (tenant_id, id) ON DELETE CASCADE,
        CONSTRAINT fk_branch_theme_overrides_tenants_tenant_id FOREIGN KEY (tenant_id) REFERENCES tenancy.tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261002100000_AddBrandAppearanceAndBranchThemeOverrides') THEN
    CREATE UNIQUE INDEX ix_brand_appearances_tenant_id_brand_id ON tenancy.brand_appearances (tenant_id, brand_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261002100000_AddBrandAppearanceAndBranchThemeOverrides') THEN
    CREATE UNIQUE INDEX ix_branch_theme_overrides_tenant_id_branch_id ON tenancy.branch_theme_overrides (tenant_id, branch_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261002100000_AddBrandAppearanceAndBranchThemeOverrides') THEN

                    ALTER TABLE tenancy.brand_appearances ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE tenancy.brand_appearances FORCE ROW LEVEL SECURITY;

                    ALTER TABLE tenancy.branch_theme_overrides ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE tenancy.branch_theme_overrides FORCE ROW LEVEL SECURITY;

                    DROP POLICY IF EXISTS brand_appearance_isolation_policy ON tenancy.brand_appearances;
                    CREATE POLICY brand_appearance_isolation_policy ON tenancy.brand_appearances
                        FOR ALL
                        USING (tenant_id = tenancy.get_current_tenant_id())
                        WITH CHECK (tenant_id = tenancy.get_current_tenant_id());

                    DROP POLICY IF EXISTS branch_theme_override_isolation_policy ON tenancy.branch_theme_overrides;
                    CREATE POLICY branch_theme_override_isolation_policy ON tenancy.branch_theme_overrides
                        FOR ALL
                        USING (tenant_id = tenancy.get_current_tenant_id())
                        WITH CHECK (tenant_id = tenancy.get_current_tenant_id());

                    GRANT SELECT, INSERT, UPDATE, DELETE ON tenancy.brand_appearances TO restaurant_app_runtime;
                    GRANT SELECT, INSERT, UPDATE, DELETE ON tenancy.branch_theme_overrides TO restaurant_app_runtime;

    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261002100000_AddBrandAppearanceAndBranchThemeOverrides') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261002100000_AddBrandAppearanceAndBranchThemeOverrides', '10.0.4');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261002120000_AddNavigationConfigToBrandAppearance') THEN
    ALTER TABLE tenancy.brand_appearances ADD navigation_config_json character varying(4000);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261002120000_AddNavigationConfigToBrandAppearance') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261002120000_AddNavigationConfigToBrandAppearance', '10.0.4');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261002140000_AddBranchSettingsAndOperatingHours') THEN
    CREATE TABLE tenancy.branch_settings (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        branch_id uuid NOT NULL,
        timezone character varying(100) NOT NULL,
        currency character varying(3) NOT NULL,
        default_locale character varying(10) NOT NULL,
        supported_locales character varying(1000) NOT NULL,
        prices_include_tax boolean NOT NULL,
        default_tax_rate_bps integer NOT NULL,
        is_service_charge_enabled boolean NOT NULL,
        service_charge_rate_bps integer NOT NULL,
        is_order_taking_enabled boolean NOT NULL,
        display_name character varying(200),
        phone_number character varying(50),
        email character varying(100),
        address character varying(500),
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone,
        concurrency_token uuid NOT NULL,
        CONSTRAINT "PK_branch_settings" PRIMARY KEY (id),
        CONSTRAINT fk_branch_settings_branches_tenant_id_branch_id FOREIGN KEY (tenant_id, branch_id) REFERENCES tenancy.branches (tenant_id, id) ON DELETE CASCADE,
        CONSTRAINT fk_branch_settings_tenants_tenant_id FOREIGN KEY (tenant_id) REFERENCES tenancy.tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261002140000_AddBranchSettingsAndOperatingHours') THEN
    CREATE TABLE tenancy.branch_operating_hours (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        branch_id uuid NOT NULL,
        schedule_json character varying(8000) NOT NULL,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone,
        concurrency_token uuid NOT NULL,
        CONSTRAINT "PK_branch_operating_hours" PRIMARY KEY (id),
        CONSTRAINT fk_branch_operating_hours_branches_tenant_id_branch_id FOREIGN KEY (tenant_id, branch_id) REFERENCES tenancy.branches (tenant_id, id) ON DELETE CASCADE,
        CONSTRAINT fk_branch_operating_hours_tenants_tenant_id FOREIGN KEY (tenant_id) REFERENCES tenancy.tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261002140000_AddBranchSettingsAndOperatingHours') THEN
    CREATE UNIQUE INDEX ix_branch_settings_tenant_id_branch_id ON tenancy.branch_settings (tenant_id, branch_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261002140000_AddBranchSettingsAndOperatingHours') THEN
    CREATE UNIQUE INDEX ix_branch_operating_hours_tenant_id_branch_id ON tenancy.branch_operating_hours (tenant_id, branch_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261002140000_AddBranchSettingsAndOperatingHours') THEN

                    ALTER TABLE tenancy.branch_settings ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE tenancy.branch_settings FORCE ROW LEVEL SECURITY;

                    ALTER TABLE tenancy.branch_operating_hours ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE tenancy.branch_operating_hours FORCE ROW LEVEL SECURITY;

                    DROP POLICY IF EXISTS branch_settings_isolation_policy ON tenancy.branch_settings;
                    CREATE POLICY branch_settings_isolation_policy ON tenancy.branch_settings
                        FOR ALL
                        USING (tenant_id = tenancy.get_current_tenant_id())
                        WITH CHECK (tenant_id = tenancy.get_current_tenant_id());

                    DROP POLICY IF EXISTS branch_operating_hours_isolation_policy ON tenancy.branch_operating_hours;
                    CREATE POLICY branch_operating_hours_isolation_policy ON tenancy.branch_operating_hours
                        FOR ALL
                        USING (tenant_id = tenancy.get_current_tenant_id())
                        WITH CHECK (tenant_id = tenancy.get_current_tenant_id());

                    DO $$
                    BEGIN
                        IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'restaurant_app_runtime') THEN
                            GRANT SELECT, INSERT, UPDATE, DELETE ON tenancy.branch_settings TO restaurant_app_runtime;
                            GRANT SELECT, INSERT, UPDATE, DELETE ON tenancy.branch_operating_hours TO restaurant_app_runtime;
                        END IF;
                    END $$;

    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261002140000_AddBranchSettingsAndOperatingHours') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261002140000_AddBranchSettingsAndOperatingHours', '10.0.4');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261003140048_AddDiningAreasStationsAndFeatureFlags') THEN
    CREATE TABLE tenancy.branch_feature_flags (
        tenant_id uuid NOT NULL,
        branch_id uuid NOT NULL,
        overrides_json character varying(4000) NOT NULL,
        concurrency_token uuid NOT NULL,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone,
        CONSTRAINT "PK_branch_feature_flags" PRIMARY KEY (tenant_id, branch_id),
        CONSTRAINT fk_branch_feature_flags_branches_tenant_id_branch_id FOREIGN KEY (tenant_id, branch_id) REFERENCES tenancy.branches (tenant_id, id) ON DELETE CASCADE,
        CONSTRAINT fk_branch_feature_flags_tenants_tenant_id FOREIGN KEY (tenant_id) REFERENCES tenancy.tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261003140048_AddDiningAreasStationsAndFeatureFlags') THEN
    CREATE TABLE tenancy.dining_areas (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        branch_id uuid NOT NULL,
        name character varying(100) NOT NULL,
        code character varying(50) NOT NULL,
        area_type integer NOT NULL,
        sort_order integer NOT NULL,
        is_active boolean NOT NULL,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone,
        concurrency_token uuid NOT NULL,
        CONSTRAINT "PK_dining_areas" PRIMARY KEY (id),
        CONSTRAINT fk_dining_areas_branches_tenant_id_branch_id FOREIGN KEY (tenant_id, branch_id) REFERENCES tenancy.branches (tenant_id, id) ON DELETE CASCADE,
        CONSTRAINT fk_dining_areas_tenants_tenant_id FOREIGN KEY (tenant_id) REFERENCES tenancy.tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261003140048_AddDiningAreasStationsAndFeatureFlags') THEN
    CREATE TABLE tenancy.preparation_stations (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        branch_id uuid NOT NULL,
        code character varying(50) NOT NULL,
        display_name character varying(100) NOT NULL,
        station_type integer NOT NULL,
        sort_order integer NOT NULL,
        is_active boolean NOT NULL,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone,
        concurrency_token uuid NOT NULL,
        CONSTRAINT "PK_preparation_stations" PRIMARY KEY (id),
        CONSTRAINT fk_preparation_stations_branches_tenant_id_branch_id FOREIGN KEY (tenant_id, branch_id) REFERENCES tenancy.branches (tenant_id, id) ON DELETE CASCADE,
        CONSTRAINT fk_preparation_stations_tenants_tenant_id FOREIGN KEY (tenant_id) REFERENCES tenancy.tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261003140048_AddDiningAreasStationsAndFeatureFlags') THEN
    CREATE TABLE tenancy.tenant_feature_flags (
        tenant_id uuid NOT NULL,
        flags_json character varying(4000) NOT NULL,
        concurrency_token uuid NOT NULL,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone,
        CONSTRAINT "PK_tenant_feature_flags" PRIMARY KEY (tenant_id),
        CONSTRAINT fk_tenant_feature_flags_tenants_tenant_id FOREIGN KEY (tenant_id) REFERENCES tenancy.tenants (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261003140048_AddDiningAreasStationsAndFeatureFlags') THEN
    CREATE UNIQUE INDEX ix_dining_areas_tenant_id_branch_id_code ON tenancy.dining_areas (tenant_id, branch_id, code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261003140048_AddDiningAreasStationsAndFeatureFlags') THEN
    CREATE INDEX ix_dining_areas_tenant_id_branch_id_sort_order ON tenancy.dining_areas (tenant_id, branch_id, sort_order);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261003140048_AddDiningAreasStationsAndFeatureFlags') THEN
    CREATE UNIQUE INDEX ix_preparation_stations_tenant_id_branch_id_code ON tenancy.preparation_stations (tenant_id, branch_id, code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261003140048_AddDiningAreasStationsAndFeatureFlags') THEN
    CREATE INDEX ix_preparation_stations_tenant_id_branch_id_sort_order ON tenancy.preparation_stations (tenant_id, branch_id, sort_order);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261003140048_AddDiningAreasStationsAndFeatureFlags') THEN

                    ALTER TABLE tenancy.dining_areas ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE tenancy.dining_areas FORCE ROW LEVEL SECURITY;

                    ALTER TABLE tenancy.preparation_stations ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE tenancy.preparation_stations FORCE ROW LEVEL SECURITY;

                    ALTER TABLE tenancy.tenant_feature_flags ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE tenancy.tenant_feature_flags FORCE ROW LEVEL SECURITY;

                    ALTER TABLE tenancy.branch_feature_flags ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE tenancy.branch_feature_flags FORCE ROW LEVEL SECURITY;

                    DROP POLICY IF EXISTS dining_areas_isolation_policy ON tenancy.dining_areas;
                    CREATE POLICY dining_areas_isolation_policy ON tenancy.dining_areas
                        FOR ALL
                        USING (tenant_id = tenancy.get_current_tenant_id())
                        WITH CHECK (tenant_id = tenancy.get_current_tenant_id());

                    DROP POLICY IF EXISTS preparation_stations_isolation_policy ON tenancy.preparation_stations;
                    CREATE POLICY preparation_stations_isolation_policy ON tenancy.preparation_stations
                        FOR ALL
                        USING (tenant_id = tenancy.get_current_tenant_id())
                        WITH CHECK (tenant_id = tenancy.get_current_tenant_id());

                    DROP POLICY IF EXISTS tenant_feature_flags_isolation_policy ON tenancy.tenant_feature_flags;
                    CREATE POLICY tenant_feature_flags_isolation_policy ON tenancy.tenant_feature_flags
                        FOR ALL
                        USING (tenant_id = tenancy.get_current_tenant_id())
                        WITH CHECK (tenant_id = tenancy.get_current_tenant_id());

                    DROP POLICY IF EXISTS branch_feature_flags_isolation_policy ON tenancy.branch_feature_flags;
                    CREATE POLICY branch_feature_flags_isolation_policy ON tenancy.branch_feature_flags
                        FOR ALL
                        USING (tenant_id = tenancy.get_current_tenant_id())
                        WITH CHECK (tenant_id = tenancy.get_current_tenant_id());

                    DO $$
                    BEGIN
                        IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'restaurant_app_runtime') THEN
                            GRANT SELECT, INSERT, UPDATE, DELETE ON tenancy.dining_areas TO restaurant_app_runtime;
                            GRANT SELECT, INSERT, UPDATE, DELETE ON tenancy.preparation_stations TO restaurant_app_runtime;
                            GRANT SELECT, INSERT, UPDATE, DELETE ON tenancy.tenant_feature_flags TO restaurant_app_runtime;
                            GRANT SELECT, INSERT, UPDATE, DELETE ON tenancy.branch_feature_flags TO restaurant_app_runtime;
                        END IF;
                    END $$;

    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261003140048_AddDiningAreasStationsAndFeatureFlags') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261003140048_AddDiningAreasStationsAndFeatureFlags', '10.0.4');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261004140337_AddMenusAndCategories') THEN
    CREATE TABLE tenancy.menus (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        branch_id uuid NOT NULL,
        name character varying(100) NOT NULL,
        slug character varying(50) NOT NULL,
        description character varying(500),
        status integer NOT NULL,
        sort_order integer NOT NULL,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone,
        concurrency_token uuid NOT NULL,
        CONSTRAINT "PK_menus" PRIMARY KEY (id),
        CONSTRAINT "AK_menus_tenant_id_id" UNIQUE (tenant_id, id),
        CONSTRAINT fk_menus_branches_tenant_id_branch_id FOREIGN KEY (tenant_id, branch_id) REFERENCES tenancy.branches (tenant_id, id) ON DELETE CASCADE,
        CONSTRAINT fk_menus_tenants_tenant_id FOREIGN KEY (tenant_id) REFERENCES tenancy.tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261004140337_AddMenusAndCategories') THEN
    CREATE TABLE tenancy.menu_categories (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        branch_id uuid NOT NULL,
        menu_id uuid NOT NULL,
        name character varying(100) NOT NULL,
        slug character varying(50) NOT NULL,
        description character varying(500),
        sort_order integer NOT NULL,
        is_active boolean NOT NULL,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone,
        concurrency_token uuid NOT NULL,
        CONSTRAINT "PK_menu_categories" PRIMARY KEY (id),
        CONSTRAINT fk_menu_categories_branches_tenant_id_branch_id FOREIGN KEY (tenant_id, branch_id) REFERENCES tenancy.branches (tenant_id, id) ON DELETE CASCADE,
        CONSTRAINT fk_menu_categories_menus_tenant_id_menu_id FOREIGN KEY (tenant_id, menu_id) REFERENCES tenancy.menus (tenant_id, id) ON DELETE CASCADE,
        CONSTRAINT fk_menu_categories_tenants_tenant_id FOREIGN KEY (tenant_id) REFERENCES tenancy.tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261004140337_AddMenusAndCategories') THEN
    CREATE INDEX "IX_menu_categories_tenant_id_branch_id" ON tenancy.menu_categories (tenant_id, branch_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261004140337_AddMenusAndCategories') THEN
    CREATE UNIQUE INDEX ix_menu_categories_tenant_id_menu_id_slug ON tenancy.menu_categories (tenant_id, menu_id, slug);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261004140337_AddMenusAndCategories') THEN
    CREATE INDEX ix_menu_categories_tenant_id_menu_id_sort_order ON tenancy.menu_categories (tenant_id, menu_id, sort_order);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261004140337_AddMenusAndCategories') THEN
    CREATE UNIQUE INDEX ix_menus_tenant_id_branch_id_slug ON tenancy.menus (tenant_id, branch_id, slug);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261004140337_AddMenusAndCategories') THEN
    CREATE INDEX ix_menus_tenant_id_branch_id_sort_order ON tenancy.menus (tenant_id, branch_id, sort_order);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261004140337_AddMenusAndCategories') THEN

                    ALTER TABLE tenancy.menus ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE tenancy.menus FORCE ROW LEVEL SECURITY;

                    ALTER TABLE tenancy.menu_categories ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE tenancy.menu_categories FORCE ROW LEVEL SECURITY;

                    DROP POLICY IF EXISTS menus_isolation_policy ON tenancy.menus;
                    CREATE POLICY menus_isolation_policy ON tenancy.menus
                        FOR ALL
                        USING (tenant_id = tenancy.get_current_tenant_id())
                        WITH CHECK (tenant_id = tenancy.get_current_tenant_id());

                    DROP POLICY IF EXISTS menu_categories_isolation_policy ON tenancy.menu_categories;
                    CREATE POLICY menu_categories_isolation_policy ON tenancy.menu_categories
                        FOR ALL
                        USING (tenant_id = tenancy.get_current_tenant_id())
                        WITH CHECK (tenant_id = tenancy.get_current_tenant_id());

                    DO $$
                    BEGIN
                        IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'restaurant_app_runtime') THEN
                            GRANT SELECT, INSERT, UPDATE, DELETE ON tenancy.menus TO restaurant_app_runtime;
                            GRANT SELECT, INSERT, UPDATE, DELETE ON tenancy.menu_categories TO restaurant_app_runtime;
                        END IF;
                    END $$;

    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261004140337_AddMenusAndCategories') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261004140337_AddMenusAndCategories', '10.0.4');
    END IF;
END $EF$;
COMMIT;
