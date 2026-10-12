using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantOrder.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRestaurantTablesAndLayout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_dining_areas_tenant_id_branch_id_id",
                schema: "tenancy",
                table: "dining_areas",
                columns: new[] { "tenant_id", "branch_id", "id" });

            migrationBuilder.CreateTable(
                name: "restaurant_tables",
                schema: "tenancy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dining_area_id = table.Column<Guid>(type: "uuid", nullable: false),
                    table_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    capacity = table.Column<int>(type: "integer", nullable: false),
                    position_x = table.Column<int>(type: "integer", nullable: false),
                    position_y = table.Column<int>(type: "integer", nullable: false),
                    width = table.Column<int>(type: "integer", nullable: false),
                    height = table.Column<int>(type: "integer", nullable: false),
                    rotation_degrees = table.Column<int>(type: "integer", nullable: false),
                    shape = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    qr_version = table.Column<int>(type: "integer", nullable: false),
                    concurrency_token = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_restaurant_tables", x => x.id);
                    table.UniqueConstraint("AK_restaurant_tables_tenant_id_branch_id_id", x => new { x.tenant_id, x.branch_id, x.id });
                    table.CheckConstraint("ck_restaurant_tables_capacity", "capacity >= 1 AND capacity <= 100");
                    table.CheckConstraint("ck_restaurant_tables_coordinates", "position_x >= 0 AND position_x <= 10000 AND position_y >= 0 AND position_y <= 10000");
                    table.CheckConstraint("ck_restaurant_tables_dimensions", "width >= 10 AND width <= 5000 AND height >= 10 AND height <= 5000");
                    table.CheckConstraint("ck_restaurant_tables_qr_version", "qr_version >= 1");
                    table.CheckConstraint("ck_restaurant_tables_rotation", "rotation_degrees >= 0 AND rotation_degrees < 360");
                    table.ForeignKey(
                        name: "fk_restaurant_tables_branches_tenant_id_branch_id",
                        columns: x => new { x.tenant_id, x.branch_id },
                        principalSchema: "tenancy",
                        principalTable: "branches",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_restaurant_tables_dining_areas_tenant_branch_area",
                        columns: x => new { x.tenant_id, x.branch_id, x.dining_area_id },
                        principalSchema: "tenancy",
                        principalTable: "dining_areas",
                        principalColumns: new[] { "tenant_id", "branch_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_restaurant_tables_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "tenancy",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_restaurant_tables_tenant_id_branch_id_dining_area_id",
                schema: "tenancy",
                table: "restaurant_tables",
                columns: new[] { "tenant_id", "branch_id", "dining_area_id" });

            migrationBuilder.CreateIndex(
                name: "ix_restaurant_tables_tenant_id_branch_id_table_number",
                schema: "tenancy",
                table: "restaurant_tables",
                columns: new[] { "tenant_id", "branch_id", "table_number" },
                unique: true);

            migrationBuilder.Sql(@"
                ALTER TABLE tenancy.restaurant_tables ENABLE ROW LEVEL SECURITY;
                ALTER TABLE tenancy.restaurant_tables FORCE ROW LEVEL SECURITY;

                DROP POLICY IF EXISTS restaurant_tables_isolation_policy ON tenancy.restaurant_tables;
                CREATE POLICY restaurant_tables_isolation_policy ON tenancy.restaurant_tables
                    FOR ALL
                    USING (tenant_id = tenancy.get_current_tenant_id())
                    WITH CHECK (tenant_id = tenancy.get_current_tenant_id());

                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'restaurant_app_runtime') THEN
                        GRANT SELECT, INSERT, UPDATE, DELETE ON tenancy.restaurant_tables TO restaurant_app_runtime;
                    END IF;
                END $$;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP POLICY IF EXISTS restaurant_tables_isolation_policy ON tenancy.restaurant_tables;
            ");

            migrationBuilder.DropTable(
                name: "restaurant_tables",
                schema: "tenancy");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_dining_areas_tenant_id_branch_id_id",
                schema: "tenancy",
                table: "dining_areas");
        }
    }
}
