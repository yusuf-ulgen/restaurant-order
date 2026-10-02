using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantOrder.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIdentityNotificationOutboxAndScopedRevocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Create transactional identity notifications outbox table
            migrationBuilder.CreateTable(
                name: "identity_notifications_outbox",
                schema: "iam",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    notification_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    recipient_email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    encrypted_payload = table.Column<string>(type: "text", nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    attempt_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    max_attempts = table.Column<int>(type: "integer", nullable: false, defaultValue: 5),
                    next_attempt_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_error = table.Column<string>(type: "text", nullable: true),
                    claim_token = table.Column<Guid>(type: "uuid", nullable: true),
                    claimed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    locked_until_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    delivered_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_identity_notifications_outbox", x => x.id);
                    table.ForeignKey(
                        name: "fk_identity_notifications_outbox_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "tenancy",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_identity_notifications_outbox_tenant_id",
                schema: "iam",
                table: "identity_notifications_outbox",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "uq_identity_notifications_outbox_idempotency_key",
                schema: "iam",
                table: "identity_notifications_outbox",
                column: "idempotency_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_identity_notifications_outbox_status_next_attempt",
                schema: "iam",
                table: "identity_notifications_outbox",
                columns: new[] { "status", "next_attempt_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_identity_notifications_outbox_status_locked_until",
                schema: "iam",
                table: "identity_notifications_outbox",
                columns: new[] { "status", "locked_until_utc" });

            // 2. Row-Level Security on outbox table
            migrationBuilder.Sql(@"
                ALTER TABLE iam.identity_notifications_outbox ENABLE ROW LEVEL SECURITY;
                ALTER TABLE iam.identity_notifications_outbox FORCE ROW LEVEL SECURITY;

                DROP POLICY IF EXISTS identity_notifications_outbox_isolation_policy ON iam.identity_notifications_outbox;
                CREATE POLICY identity_notifications_outbox_isolation_policy ON iam.identity_notifications_outbox
                    FOR ALL
                    USING (tenant_id = tenancy.get_current_tenant_id())
                    WITH CHECK (tenant_id = tenancy.get_current_tenant_id());

                GRANT SELECT, INSERT, UPDATE, DELETE ON iam.identity_notifications_outbox TO restaurant_app_runtime;
            ");

            // 3. PostgreSQL claim function with FOR UPDATE SKIP LOCKED and Lease Recovery
            migrationBuilder.Sql(@"
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
            ");

            // 4. PostgreSQL functions to complete or fail outbox notifications with claim validation
            migrationBuilder.Sql(@"
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
            ");

            // 5. PostgreSQL function for scoped session revocation on membership status updates
            migrationBuilder.Sql(@"
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
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS iam.revoke_membership_sessions(uuid, uuid, uuid, timestamp with time zone);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS iam.fail_identity_notification(uuid, uuid, text, timestamp with time zone, boolean, timestamp with time zone);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS iam.fail_identity_notification(uuid, text, timestamp with time zone, boolean, timestamp with time zone);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS iam.complete_identity_notification(uuid, uuid, timestamp with time zone);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS iam.complete_identity_notification(uuid, timestamp with time zone);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS iam.claim_pending_identity_notifications(integer, timestamp with time zone, integer, uuid);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS iam.claim_pending_identity_notifications(integer, timestamp with time zone);");

            migrationBuilder.DropTable(
                name: "identity_notifications_outbox",
                schema: "iam");
        }
    }
}
