using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantOrder.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationDeliveriesTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "notification_deliveries",
                schema: "tenancy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    device_registration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    installation_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    recipient_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    recipient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    max_attempts = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lock_claim_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    lock_expires_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_deliveries", x => x.id);
                    table.ForeignKey(
                        name: "fk_notification_deliveries_branches_tenant_id_branch_id",
                        columns: x => new { x.tenant_id, x.branch_id },
                        principalSchema: "tenancy",
                        principalTable: "branches",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_notification_deliveries_device_registrations_device_id",
                        column: x => x.device_registration_id,
                        principalSchema: "tenancy",
                        principalTable: "device_registrations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_notification_deliveries_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "tenancy",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_notification_deliveries_device_registration_id",
                schema: "tenancy",
                table: "notification_deliveries",
                column: "device_registration_id");

            migrationBuilder.CreateIndex(
                name: "IX_notification_deliveries_tenant_id_branch_id",
                schema: "tenancy",
                table: "notification_deliveries",
                columns: new[] { "tenant_id", "branch_id" });

            migrationBuilder.CreateIndex(
                name: "ix_notification_deliveries_tenant_id_device_registration_id",
                schema: "tenancy",
                table: "notification_deliveries",
                columns: new[] { "tenant_id", "device_registration_id" });

            migrationBuilder.CreateIndex(
                name: "ix_notification_deliveries_tenant_id_event_id",
                schema: "tenancy",
                table: "notification_deliveries",
                columns: new[] { "tenant_id", "event_id" });

            migrationBuilder.CreateIndex(
                name: "ix_notification_deliveries_tenant_id_status_next_attempt",
                schema: "tenancy",
                table: "notification_deliveries",
                columns: new[] { "tenant_id", "status", "next_attempt_utc" });

            migrationBuilder.Sql(@"
                ALTER TABLE tenancy.notification_deliveries ENABLE ROW LEVEL SECURITY;
                ALTER TABLE tenancy.notification_deliveries FORCE ROW LEVEL SECURITY;

                DROP POLICY IF EXISTS notification_deliveries_isolation_policy ON tenancy.notification_deliveries;
                CREATE POLICY notification_deliveries_isolation_policy ON tenancy.notification_deliveries
                    FOR ALL
                    USING (tenant_id = tenancy.get_current_tenant_id())
                    WITH CHECK (tenant_id = tenancy.get_current_tenant_id());

                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'restaurant_app_runtime') THEN
                        GRANT SELECT, INSERT, UPDATE, DELETE ON tenancy.notification_deliveries TO restaurant_app_runtime;
                    END IF;
                END $$;

                CREATE OR REPLACE FUNCTION tenancy.claim_pending_notification_deliveries(
                    p_limit integer,
                    p_now_utc timestamp with time zone,
                    p_lease_seconds integer,
                    p_claim_id text,
                    p_tenant_id uuid DEFAULT NULL
                )
                RETURNS TABLE (
                    id uuid,
                    tenant_id uuid,
                    branch_id uuid,
                    event_id uuid,
                    event_type text,
                    device_registration_id uuid,
                    installation_id text,
                    recipient_type text,
                    recipient_id uuid,
                    status integer,
                    attempt_count integer,
                    max_attempts integer,
                    expires_at timestamp with time zone,
                    lock_claim_id text
                )
                LANGUAGE plpgsql
                SECURITY DEFINER
                SET search_path = tenancy, pg_temp
                AS $func$
                BEGIN
                    RETURN QUERY
                    WITH cte AS (
                        SELECT d.id
                        FROM tenancy.notification_deliveries d
                        WHERE ((d.status = 0 AND (d.next_attempt_utc IS NULL OR d.next_attempt_utc <= p_now_utc))
                            OR (d.status = 3 AND (d.next_attempt_utc IS NULL OR d.next_attempt_utc <= p_now_utc))
                            OR (d.status = 1 AND d.lock_expires_utc < p_now_utc))
                          AND (p_tenant_id IS NULL OR d.tenant_id = p_tenant_id)
                        ORDER BY d.created_at ASC
                        LIMIT p_limit
                        FOR UPDATE SKIP LOCKED
                    )
                    UPDATE tenancy.notification_deliveries d
                    SET status = 1,
                        lock_claim_id = p_claim_id,
                        lock_expires_utc = p_now_utc + (p_lease_seconds || ' seconds')::interval,
                        attempt_count = d.attempt_count + 1
                    FROM cte
                    WHERE d.id = cte.id
                    RETURNING d.id, d.tenant_id, d.branch_id, d.event_id, d.event_type::text, d.device_registration_id,
                              d.installation_id::text, d.recipient_type::text, d.recipient_id, d.status,
                              d.attempt_count, d.max_attempts, d.expires_at, d.lock_claim_id::text;
                END;
                $func$;

                REVOKE ALL ON FUNCTION tenancy.claim_pending_notification_deliveries(integer, timestamp with time zone, integer, text, uuid) FROM PUBLIC;
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'restaurant_app_runtime') THEN
                        GRANT EXECUTE ON FUNCTION tenancy.claim_pending_notification_deliveries(integer, timestamp with time zone, integer, text, uuid) TO restaurant_app_runtime;
                    END IF;
                END $$;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP FUNCTION IF EXISTS tenancy.claim_pending_notification_deliveries(integer, timestamp with time zone, integer, text, uuid);
                DROP POLICY IF EXISTS notification_deliveries_isolation_policy ON tenancy.notification_deliveries;
            ");

            migrationBuilder.DropTable(
                name: "notification_deliveries",
                schema: "tenancy");
        }
    }
}
