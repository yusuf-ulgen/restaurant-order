using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantOrder.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIamPreAuthBootstrapFunctionsAndTransactions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Dedicated pre-authentication tenant lookup by slug (ADR-0010 model)
            migrationBuilder.Sql(@"
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
            ");

            // 2. Pre-authentication membership resolution for login
            migrationBuilder.Sql(@"
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
            ");

            // 3. Pre-authentication invitation token lookup
            migrationBuilder.Sql(@"
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
            ");

            // 4. Pre-authentication password reset token lookup
            migrationBuilder.Sql(@"
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
            ");

            // 5. Pre-authentication trusted terminal lookup for PIN authentication
            migrationBuilder.Sql(@"
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
            ");

            // 6. Pre-authentication user active membership lookup (e.g. forgot password tenant resolution)
            migrationBuilder.Sql(@"
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
            ");

            // 7. Lookup user by ID (minimal projection, no password hash)
            migrationBuilder.Sql(@"
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
            ");

            // 8. Lookup session tenant for logout/revocation before tenant context is set
            migrationBuilder.Sql(@"
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
            ");

            // 9. Revoke PUBLIC from lookup_user_for_login to guarantee least privilege
            migrationBuilder.Sql(@"
                REVOKE ALL ON FUNCTION iam.lookup_user_for_login(text) FROM PUBLIC;
                GRANT EXECUTE ON FUNCTION iam.lookup_user_for_login(text) TO restaurant_app_runtime;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP FUNCTION IF EXISTS iam.lookup_session_tenant(uuid);
                DROP FUNCTION IF EXISTS iam.lookup_user_by_id(uuid);
                DROP FUNCTION IF EXISTS iam.lookup_first_active_membership_by_user(uuid);
                DROP FUNCTION IF EXISTS iam.lookup_terminal_for_auth(uuid);
                DROP FUNCTION IF EXISTS iam.lookup_password_reset_token(text);
                DROP FUNCTION IF EXISTS iam.lookup_invitation_token(text);
                DROP FUNCTION IF EXISTS iam.lookup_membership_for_login(uuid, uuid);
                DROP FUNCTION IF EXISTS iam.lookup_tenant_by_slug(text);
            ");
        }
    }
}
