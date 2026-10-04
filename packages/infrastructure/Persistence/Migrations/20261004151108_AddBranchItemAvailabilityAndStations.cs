using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantOrder.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBranchItemAvailabilityAndStations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "preparation_station_id",
                schema: "tenancy",
                table: "menu_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "branch_item_availabilities",
                schema: "tenancy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    menu_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_variant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_available = table.Column<bool>(type: "boolean", nullable: false),
                    reason_code = table.Column<int>(type: "integer", nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    expected_available_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    changed_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    changed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    concurrency_token = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_branch_item_availabilities", x => x.id);
                    table.ForeignKey(
                        name: "fk_branch_item_availabilities_branches_tenant_id_branch_id",
                        columns: x => new { x.tenant_id, x.branch_id },
                        principalSchema: "tenancy",
                        principalTable: "branches",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_branch_item_availabilities_item_variants_variant_id",
                        column: x => x.item_variant_id,
                        principalSchema: "tenancy",
                        principalTable: "item_variants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_branch_item_availabilities_menu_items_item_id",
                        column: x => x.menu_item_id,
                        principalSchema: "tenancy",
                        principalTable: "menu_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_menu_items_preparation_station_id",
                schema: "tenancy",
                table: "menu_items",
                column: "preparation_station_id");

            migrationBuilder.CreateIndex(
                name: "IX_branch_item_availabilities_item_variant_id",
                schema: "tenancy",
                table: "branch_item_availabilities",
                column: "item_variant_id");

            migrationBuilder.CreateIndex(
                name: "IX_branch_item_availabilities_menu_item_id",
                schema: "tenancy",
                table: "branch_item_availabilities",
                column: "menu_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_branch_item_availabilities_item_unique",
                schema: "tenancy",
                table: "branch_item_availabilities",
                columns: new[] { "tenant_id", "branch_id", "menu_item_id" },
                unique: true,
                filter: "item_variant_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_branch_item_availabilities_variant_unique",
                schema: "tenancy",
                table: "branch_item_availabilities",
                columns: new[] { "tenant_id", "branch_id", "menu_item_id", "item_variant_id" },
                unique: true,
                filter: "item_variant_id IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "fk_menu_items_preparation_stations_station_id",
                schema: "tenancy",
                table: "menu_items",
                column: "preparation_station_id",
                principalSchema: "tenancy",
                principalTable: "preparation_stations",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.Sql(@"
                ALTER TABLE tenancy.branch_item_availabilities ENABLE ROW LEVEL SECURITY;
                ALTER TABLE tenancy.branch_item_availabilities FORCE ROW LEVEL SECURITY;

                DROP POLICY IF EXISTS branch_item_availabilities_isolation_policy ON tenancy.branch_item_availabilities;
                CREATE POLICY branch_item_availabilities_isolation_policy ON tenancy.branch_item_availabilities
                    FOR ALL
                    USING (tenant_id = tenancy.get_current_tenant_id())
                    WITH CHECK (tenant_id = tenancy.get_current_tenant_id());

                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'restaurant_app_runtime') THEN
                        GRANT SELECT, INSERT, UPDATE, DELETE ON tenancy.branch_item_availabilities TO restaurant_app_runtime;
                    END IF;
                END $$;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP POLICY IF EXISTS branch_item_availabilities_isolation_policy ON tenancy.branch_item_availabilities;
                ALTER TABLE tenancy.branch_item_availabilities NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE tenancy.branch_item_availabilities DISABLE ROW LEVEL SECURITY;
            ");

            migrationBuilder.DropForeignKey(
                name: "fk_menu_items_preparation_stations_station_id",
                schema: "tenancy",
                table: "menu_items");

            migrationBuilder.DropTable(
                name: "branch_item_availabilities",
                schema: "tenancy");

            migrationBuilder.DropIndex(
                name: "IX_menu_items_preparation_station_id",
                schema: "tenancy",
                table: "menu_items");

            migrationBuilder.DropColumn(
                name: "preparation_station_id",
                schema: "tenancy",
                table: "menu_items");
        }
    }
}
