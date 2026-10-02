using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantOrder.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIamRowLevelSecurity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Verify required cluster-level unprivileged runtime group role exists
            migrationBuilder.Sql(@"
                DO $CHECK$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'restaurant_app_runtime') THEN
                        RAISE EXCEPTION 'Required group role ""restaurant_app_runtime"" does not exist. Ensure database bootstrap script (deploy/bootstrap/001_create_runtime_login_role.sql) has been executed by a privileged administrator prior to applying schema migrations.';
                    END IF;
                END $CHECK$;
            ");

            // 2. Enable and FORCE Row-Level Security on all tenant-owned IAM tables
            migrationBuilder.Sql(@"
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
            ");

            // 3. Create RLS Policies for tenant isolation via tenancy.get_current_tenant_id()
            migrationBuilder.Sql(@"
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
            ");

            // 4. Append-Only Audit Log Protection Trigger (blocks UPDATE and DELETE at database level)
            migrationBuilder.Sql(@"
                CREATE OR REPLACE FUNCTION iam.prevent_audit_tampering() RETURNS trigger AS $$
                BEGIN
                    RAISE EXCEPTION 'Security audit log rows are append-only. UPDATE and DELETE operations are strictly prohibited.';
                END;
                $$ LANGUAGE plpgsql;

                DROP TRIGGER IF EXISTS trg_prevent_audit_tampering ON iam.security_audit_events;
                CREATE TRIGGER trg_prevent_audit_tampering
                    BEFORE UPDATE OR DELETE ON iam.security_audit_events
                    FOR EACH ROW EXECUTE FUNCTION iam.prevent_audit_tampering();
            ");

            // 5. SECURITY DEFINER Login Lookup Function (ADR-0010) with search_path pinned to (iam, pg_temp)
            migrationBuilder.Sql(@"
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
            ");

            // 6. Runtime Application Group Role Permissions (least privilege, unprivileged DML)
            migrationBuilder.Sql(@"
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
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 1. Revoke privileges
            migrationBuilder.Sql(@"
                REVOKE ALL PRIVILEGES ON ALL TABLES IN SCHEMA iam FROM restaurant_app_runtime;
                REVOKE ALL PRIVILEGES ON ALL FUNCTIONS IN SCHEMA iam FROM restaurant_app_runtime;
                REVOKE USAGE ON SCHEMA iam FROM restaurant_app_runtime;
            ");

            // 2. Drop functions and triggers
            migrationBuilder.Sql(@"
                DROP FUNCTION IF EXISTS iam.lookup_user_for_login(text);

                DROP TRIGGER IF EXISTS trg_prevent_audit_tampering ON iam.security_audit_events;
                DROP FUNCTION IF EXISTS iam.prevent_audit_tampering();
            ");

            // 3. Drop RLS policies
            migrationBuilder.Sql(@"
                DROP POLICY IF EXISTS password_reset_tokens_isolation_policy ON iam.password_reset_tokens;
                DROP POLICY IF EXISTS invitation_tokens_isolation_policy ON iam.invitation_tokens;
                DROP POLICY IF EXISTS security_audit_events_isolation_policy ON iam.security_audit_events;
                DROP POLICY IF EXISTS trusted_terminal_isolation_policy ON iam.trusted_terminals;
                DROP POLICY IF EXISTS pin_credential_isolation_policy ON iam.pin_credentials;
                DROP POLICY IF EXISTS refresh_token_isolation_policy ON iam.refresh_tokens;
                DROP POLICY IF EXISTS session_isolation_policy ON iam.sessions;
                DROP POLICY IF EXISTS membership_isolation_policy ON iam.memberships;

                ALTER TABLE iam.password_reset_tokens NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE iam.password_reset_tokens DISABLE ROW LEVEL SECURITY;
                ALTER TABLE iam.invitation_tokens NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE iam.invitation_tokens DISABLE ROW LEVEL SECURITY;
                ALTER TABLE iam.security_audit_events NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE iam.security_audit_events DISABLE ROW LEVEL SECURITY;
                ALTER TABLE iam.trusted_terminals NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE iam.trusted_terminals DISABLE ROW LEVEL SECURITY;
                ALTER TABLE iam.pin_credentials NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE iam.pin_credentials DISABLE ROW LEVEL SECURITY;
                ALTER TABLE iam.refresh_tokens NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE iam.refresh_tokens DISABLE ROW LEVEL SECURITY;
                ALTER TABLE iam.sessions NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE iam.sessions DISABLE ROW LEVEL SECURITY;
                ALTER TABLE iam.memberships NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE iam.memberships DISABLE ROW LEVEL SECURITY;
            ");
        }
    }
}
