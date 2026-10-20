using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RestaurantOrder.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOperationalOutboxAndDeviceRegistrations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "device_registrations",
                schema: "tenancy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    dining_session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    installation_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    platform = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device_registrations", x => x.id);
                    table.ForeignKey(
                        name: "fk_device_registrations_branches_tenant_id_branch_id",
                        columns: x => new { x.tenant_id, x.branch_id },
                        principalSchema: "tenancy",
                        principalTable: "branches",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_device_registrations_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "tenancy",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "operational_outbox",
                schema: "tenancy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    aggregate_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    event_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    schema_version = table.Column<int>(type: "integer", nullable: false),
                    sequence_number = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    status = table.Column<int>(type: "integer", nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    max_attempts = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    lock_claim_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    lock_expires_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    dispatched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operational_outbox", x => x.id);
                    table.ForeignKey(
                        name: "fk_operational_outbox_branches_tenant_id_branch_id",
                        columns: x => new { x.tenant_id, x.branch_id },
                        principalSchema: "tenancy",
                        principalTable: "branches",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_operational_outbox_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "tenancy",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_device_registrations_tenant_id_branch_id_is_active",
                schema: "tenancy",
                table: "device_registrations",
                columns: new[] { "tenant_id", "branch_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_device_registrations_tenant_id_installation_id",
                schema: "tenancy",
                table: "device_registrations",
                columns: new[] { "tenant_id", "installation_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_device_registrations_tenant_id_session_id_is_active",
                schema: "tenancy",
                table: "device_registrations",
                columns: new[] { "tenant_id", "dining_session_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_device_registrations_tenant_id_user_id_is_active",
                schema: "tenancy",
                table: "device_registrations",
                columns: new[] { "tenant_id", "user_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_operational_outbox_sequence_number",
                schema: "tenancy",
                table: "operational_outbox",
                column: "sequence_number");

            migrationBuilder.CreateIndex(
                name: "ix_operational_outbox_status_next_attempt",
                schema: "tenancy",
                table: "operational_outbox",
                columns: new[] { "status", "next_attempt_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_operational_outbox_tenant_id_branch_id_sequence_number",
                schema: "tenancy",
                table: "operational_outbox",
                columns: new[] { "tenant_id", "branch_id", "sequence_number" });

            migrationBuilder.CreateIndex(
                name: "ix_operational_outbox_tenant_id_idempotency_key",
                schema: "tenancy",
                table: "operational_outbox",
                columns: new[] { "tenant_id", "idempotency_key" },
                unique: true);

            migrationBuilder.Sql(@"
                ALTER TABLE tenancy.operational_outbox ENABLE ROW LEVEL SECURITY;
                ALTER TABLE tenancy.operational_outbox FORCE ROW LEVEL SECURITY;

                DROP POLICY IF EXISTS operational_outbox_isolation_policy ON tenancy.operational_outbox;
                CREATE POLICY operational_outbox_isolation_policy ON tenancy.operational_outbox
                    FOR ALL
                    USING (tenant_id = tenancy.get_current_tenant_id())
                    WITH CHECK (tenant_id = tenancy.get_current_tenant_id());

                ALTER TABLE tenancy.device_registrations ENABLE ROW LEVEL SECURITY;
                ALTER TABLE tenancy.device_registrations FORCE ROW LEVEL SECURITY;

                DROP POLICY IF EXISTS device_registrations_isolation_policy ON tenancy.device_registrations;
                CREATE POLICY device_registrations_isolation_policy ON tenancy.device_registrations
                    FOR ALL
                    USING (tenant_id = tenancy.get_current_tenant_id())
                    WITH CHECK (tenant_id = tenancy.get_current_tenant_id());

                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'restaurant_app_runtime') THEN
                        GRANT SELECT, INSERT, UPDATE, DELETE ON tenancy.operational_outbox TO restaurant_app_runtime;
                        GRANT SELECT, INSERT, UPDATE, DELETE ON tenancy.device_registrations TO restaurant_app_runtime;
                        GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA tenancy TO restaurant_app_runtime;
                    END IF;
                END $$;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP POLICY IF EXISTS operational_outbox_isolation_policy ON tenancy.operational_outbox;
                DROP POLICY IF EXISTS device_registrations_isolation_policy ON tenancy.device_registrations;
            ");

            migrationBuilder.DropTable(
                name: "device_registrations",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "operational_outbox",
                schema: "tenancy");
        }
    }
}
