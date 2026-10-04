using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantOrder.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBranchSettingsAndOperatingHours : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "branch_settings",
                schema: "tenancy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    timezone = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    default_locale = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    supported_locales = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    prices_include_tax = table.Column<bool>(type: "boolean", nullable: false),
                    default_tax_rate_bps = table.Column<int>(type: "integer", nullable: false),
                    is_service_charge_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    service_charge_rate_bps = table.Column<int>(type: "integer", nullable: false),
                    is_order_taking_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    phone_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    email = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    concurrency_token = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_branch_settings", x => x.id);
                    table.ForeignKey(
                        name: "fk_branch_settings_branches_tenant_id_branch_id",
                        columns: x => new { x.tenant_id, x.branch_id },
                        principalSchema: "tenancy",
                        principalTable: "branches",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_branch_settings_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "tenancy",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "branch_operating_hours",
                schema: "tenancy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    schedule_json = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    concurrency_token = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_branch_operating_hours", x => x.id);
                    table.ForeignKey(
                        name: "fk_branch_operating_hours_branches_tenant_id_branch_id",
                        columns: x => new { x.tenant_id, x.branch_id },
                        principalSchema: "tenancy",
                        principalTable: "branches",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_branch_operating_hours_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "tenancy",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_branch_settings_tenant_id_branch_id",
                schema: "tenancy",
                table: "branch_settings",
                columns: new[] { "tenant_id", "branch_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_branch_operating_hours_tenant_id_branch_id",
                schema: "tenancy",
                table: "branch_operating_hours",
                columns: new[] { "tenant_id", "branch_id" },
                unique: true);

            // Enable and FORCE RLS
            migrationBuilder.Sql(@"
                ALTER TABLE tenancy.branch_settings ENABLE ROW LEVEL SECURITY;
                ALTER TABLE tenancy.branch_settings FORCE ROW LEVEL SECURITY;

                ALTER TABLE tenancy.branch_operating_hours ENABLE ROW LEVEL SECURITY;
                ALTER TABLE tenancy.branch_operating_hours FORCE ROW LEVEL SECURITY;

                DROP POLICY IF EXISTS branch_settings_isolation_policy ON tenancy.branch_settings;
                CREATE POLICY branch_settings_isolation_policy ON tenancy.branch_settings
                    FOR ALL
                    USING (tenant_id = tenancy.get_current_tenant_id())
                    WITH CHECK (tenant_id = tenancy.get_current_tenant_id());

                DROP POLICY IF EXISTS branch_operating_hours_isolation_policy ON tenancy.branch_operating_hours;
                CREATE POLICY branch_operating_hours_isolation_policy ON tenancy.branch_operating_hours
                    FOR ALL
                    USING (tenant_id = tenancy.get_current_tenant_id())
                    WITH CHECK (tenant_id = tenancy.get_current_tenant_id());

                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'restaurant_app_runtime') THEN
                        GRANT SELECT, INSERT, UPDATE, DELETE ON tenancy.branch_settings TO restaurant_app_runtime;
                        GRANT SELECT, INSERT, UPDATE, DELETE ON tenancy.branch_operating_hours TO restaurant_app_runtime;
                    END IF;
                END $$;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP POLICY IF EXISTS branch_operating_hours_isolation_policy ON tenancy.branch_operating_hours;
                DROP POLICY IF EXISTS branch_settings_isolation_policy ON tenancy.branch_settings;

                ALTER TABLE tenancy.branch_operating_hours NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE tenancy.branch_operating_hours DISABLE ROW LEVEL SECURITY;

                ALTER TABLE tenancy.branch_settings NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE tenancy.branch_settings DISABLE ROW LEVEL SECURITY;
            ");

            migrationBuilder.DropTable(
                name: "branch_operating_hours",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "branch_settings",
                schema: "tenancy");
        }
    }
}