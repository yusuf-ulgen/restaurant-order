using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantOrder.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDiningSessionsAndLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "dining_sessions",
                schema: "tenancy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    table_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    guest_count = table.Column<int>(type: "integer", nullable: false),
                    assigned_waiter_id = table.Column<Guid>(type: "uuid", nullable: true),
                    opened_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    activated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    bill_requested_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    close_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    merged_into_session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    concurrency_token = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dining_sessions", x => x.id);
                    table.UniqueConstraint("AK_dining_sessions_tenant_id_branch_id_id", x => new { x.tenant_id, x.branch_id, x.id });
                    table.CheckConstraint("ck_dining_sessions_guest_count", "guest_count >= 1");
                    table.CheckConstraint("ck_dining_sessions_status", "status IN (1, 2, 3, 4)");
                    table.CheckConstraint("ck_dining_sessions_status_timestamps", "((status = 1 AND activated_at_utc IS NULL AND bill_requested_at_utc IS NULL AND closed_at_utc IS NULL) OR (status = 2 AND activated_at_utc IS NOT NULL AND closed_at_utc IS NULL) OR (status = 3 AND activated_at_utc IS NOT NULL AND bill_requested_at_utc IS NOT NULL AND closed_at_utc IS NULL) OR (status = 4 AND closed_at_utc IS NOT NULL))");
                    table.ForeignKey(
                        name: "FK_dining_sessions_dining_sessions_tenant_id_branch_id_merged_~",
                        columns: x => new { x.tenant_id, x.branch_id, x.merged_into_session_id },
                        principalSchema: "tenancy",
                        principalTable: "dining_sessions",
                        principalColumns: new[] { "tenant_id", "branch_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_dining_sessions_restaurant_tables_tenant_id_branch_id_table~",
                        columns: x => new { x.tenant_id, x.branch_id, x.table_id },
                        principalSchema: "tenancy",
                        principalTable: "restaurant_tables",
                        principalColumns: new[] { "tenant_id", "branch_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_dining_sessions_tenant_id_branch_id_merged_into_session_id",
                schema: "tenancy",
                table: "dining_sessions",
                columns: new[] { "tenant_id", "branch_id", "merged_into_session_id" });

            migrationBuilder.CreateIndex(
                name: "ix_dining_sessions_tenant_branch_status",
                schema: "tenancy",
                table: "dining_sessions",
                columns: new[] { "tenant_id", "branch_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_dining_sessions_tenant_branch_table_active",
                schema: "tenancy",
                table: "dining_sessions",
                columns: new[] { "tenant_id", "branch_id", "table_id" },
                unique: true,
                filter: "status <> 4");

            migrationBuilder.CreateIndex(
                name: "ix_dining_sessions_tenant_branch_table_created",
                schema: "tenancy",
                table: "dining_sessions",
                columns: new[] { "tenant_id", "branch_id", "table_id", "created_at_utc" });

            migrationBuilder.Sql(@"
                ALTER TABLE tenancy.dining_sessions ENABLE ROW LEVEL SECURITY;
                ALTER TABLE tenancy.dining_sessions FORCE ROW LEVEL SECURITY;

                DROP POLICY IF EXISTS dining_sessions_isolation_policy ON tenancy.dining_sessions;
                CREATE POLICY dining_sessions_isolation_policy ON tenancy.dining_sessions
                    FOR ALL
                    USING (tenant_id = tenancy.get_current_tenant_id())
                    WITH CHECK (tenant_id = tenancy.get_current_tenant_id());

                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'restaurant_app_runtime') THEN
                        GRANT SELECT, INSERT, UPDATE, DELETE ON tenancy.dining_sessions TO restaurant_app_runtime;
                    END IF;
                END $$;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "dining_sessions",
                schema: "tenancy");
        }
    }
}
