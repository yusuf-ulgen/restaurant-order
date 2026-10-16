using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantOrder.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceRequestsAndIsolation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "service_requests",
                schema: "tenancy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dining_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    table_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_type = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    idempotency_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    request_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    acknowledged_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    concurrency_token = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_service_requests", x => x.id);
                    table.CheckConstraint("ck_service_requests_status", "status IN (1, 2, 3, 4)");
                    table.CheckConstraint("ck_service_requests_type", "request_type IN (1, 2, 3, 4)");
                    table.ForeignKey(
                        name: "fk_service_requests_table",
                        columns: x => new { x.tenant_id, x.branch_id, x.table_id },
                        principalSchema: "tenancy",
                        principalTable: "restaurant_tables",
                        principalColumns: new[] { "tenant_id", "branch_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_service_requests_session",
                        columns: x => new { x.tenant_id, x.branch_id, x.dining_session_id },
                        principalSchema: "tenancy",
                        principalTable: "dining_sessions",
                        principalColumns: new[] { "tenant_id", "branch_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_service_requests_tenant_session_type_open",
                schema: "tenancy",
                table: "service_requests",
                columns: new[] { "tenant_id", "dining_session_id", "request_type" },
                unique: true,
                filter: "status = 1");

            migrationBuilder.CreateIndex(
                name: "ix_service_requests_tenant_branch_idempotency_key",
                schema: "tenancy",
                table: "service_requests",
                columns: new[] { "tenant_id", "branch_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_service_requests_tenant_session_status",
                schema: "tenancy",
                table: "service_requests",
                columns: new[] { "tenant_id", "dining_session_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_service_requests_tenant_branch_created",
                schema: "tenancy",
                table: "service_requests",
                columns: new[] { "tenant_id", "branch_id", "created_at_utc" });

            migrationBuilder.Sql(@"
                ALTER TABLE tenancy.service_requests ENABLE ROW LEVEL SECURITY;
                ALTER TABLE tenancy.service_requests FORCE ROW LEVEL SECURITY;

                DROP POLICY IF EXISTS service_requests_isolation_policy ON tenancy.service_requests;
                CREATE POLICY service_requests_isolation_policy ON tenancy.service_requests
                    FOR ALL
                    USING (tenant_id = tenancy.get_current_tenant_id())
                    WITH CHECK (tenant_id = tenancy.get_current_tenant_id());

                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'restaurant_app_runtime') THEN
                        GRANT SELECT, INSERT, UPDATE, DELETE ON tenancy.service_requests TO restaurant_app_runtime;
                    END IF;
                END $$;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP POLICY IF EXISTS service_requests_isolation_policy ON tenancy.service_requests;
            ");

            migrationBuilder.DropTable(
                name: "service_requests",
                schema: "tenancy");
        }
    }
}
