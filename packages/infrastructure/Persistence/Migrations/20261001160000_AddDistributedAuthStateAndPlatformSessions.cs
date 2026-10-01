using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantOrder.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDistributedAuthStateAndPlatformSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Create global platform sessions table (SuperAdmin sessions, decoupled from tenant RLS)
            migrationBuilder.Sql(@"
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
            ");

            // 2. Create global platform refresh tokens table (SuperAdmin tokens, decoupled from tenant RLS)
            migrationBuilder.Sql(@"
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
            ");

            // 3. Grant unprivileged runtime permissions to platform session and token tables
            // and grant foreign key references on iam.users to allow least-privilege relationship checks
            migrationBuilder.Sql(@"
                GRANT SELECT, INSERT, UPDATE, DELETE ON iam.platform_sessions TO restaurant_app_runtime;
                GRANT SELECT, INSERT, UPDATE, DELETE ON iam.platform_refresh_tokens TO restaurant_app_runtime;
                GRANT REFERENCES ON iam.users TO restaurant_app_runtime;
                GRANT SELECT (failed_login_attempts, lockout_end_utc) ON iam.users TO restaurant_app_runtime;
            ");

            // Recreate lookup_tenant_by_slug to return status text matching tenancy.tenants schema
            migrationBuilder.Sql(@"
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
            ");

            // 4. Pre-rotation lookup function for tenant refresh tokens
            migrationBuilder.Sql(@"
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
            ");

            // 5. Immediate session and security version validation function
            migrationBuilder.Sql(@"
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
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS iam.validate_token_session(uuid, uuid, integer);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS iam.lookup_refresh_token_for_rotation(text);");
            migrationBuilder.DropTable(
                name: "platform_refresh_tokens",
                schema: "iam");
            migrationBuilder.DropTable(
                name: "platform_sessions",
                schema: "iam");
        }
    }
}
