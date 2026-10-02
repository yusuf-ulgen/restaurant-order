using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantOrder.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HardenPlatformSessionsAndAtomicLockout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Revoke direct broad table access on platform sessions and tokens from runtime role
            migrationBuilder.Sql(@"
                REVOKE SELECT, INSERT, UPDATE, DELETE ON iam.platform_sessions FROM restaurant_app_runtime;
                REVOKE SELECT, INSERT, UPDATE, DELETE ON iam.platform_refresh_tokens FROM restaurant_app_runtime;
            ");

            // 2. Updated iam.validate_token_session enforcing user-to-session ownership
            migrationBuilder.Sql(@"
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
            ");

            // 3. Atomic failed login attempt recording and account lockout
            migrationBuilder.Sql(@"
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
            ");

            // 4. Dedicated least-privilege functions for platform sessions
            migrationBuilder.Sql(@"
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
            ");

            // 5. User-verified session tenant lookup (Anti-IDOR)
            migrationBuilder.Sql(@"
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
            ");

            // 6. User-verified tenant session revocation (Anti-IDOR)
            migrationBuilder.Sql(@"
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
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP FUNCTION IF EXISTS iam.revoke_tenant_session_for_user(uuid, uuid, uuid, text, timestamptz);
                DROP FUNCTION IF EXISTS iam.lookup_session_tenant_for_user(uuid, uuid);
                DROP FUNCTION IF EXISTS iam.is_platform_session_active(uuid, timestamptz);
                DROP FUNCTION IF EXISTS iam.list_active_platform_sessions(uuid, timestamptz);
                DROP FUNCTION IF EXISTS iam.revoke_all_user_platform_sessions(uuid, timestamptz);
                DROP FUNCTION IF EXISTS iam.revoke_platform_session(uuid, uuid, text, timestamptz);
                DROP FUNCTION IF EXISTS iam.rotate_platform_refresh_token(text, uuid, text, timestamptz, timestamptz);
                DROP FUNCTION IF EXISTS iam.create_platform_session(uuid, uuid, uuid, uuid, text, timestamptz, timestamptz, timestamptz, text, text);
                DROP FUNCTION IF EXISTS iam.record_failed_login_attempt(uuid, integer, integer, timestamptz);
            ");
        }
    }
}
