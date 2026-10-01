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

