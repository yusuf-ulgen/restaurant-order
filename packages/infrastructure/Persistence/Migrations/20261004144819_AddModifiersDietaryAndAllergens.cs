using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantOrder.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddModifiersDietaryAndAllergens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "allergen_tags",
                schema: "tenancy",
                table: "menu_items",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "dietary_tags",
                schema: "tenancy",
                table: "menu_items",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "spicy_level",
                schema: "tenancy",
                table: "menu_items",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "modifier_groups",
                schema: "tenancy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    min_selections = table.Column<int>(type: "integer", nullable: false),
                    max_selections = table.Column<int>(type: "integer", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    concurrency_token = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_modifier_groups", x => x.id);
                    table.UniqueConstraint("AK_modifier_groups_tenant_id_branch_id_id", x => new { x.tenant_id, x.branch_id, x.id });
                    table.ForeignKey(
                        name: "fk_modifier_groups_branches_tenant_id_branch_id",
                        columns: x => new { x.tenant_id, x.branch_id },
                        principalSchema: "tenancy",
                        principalTable: "branches",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_modifier_groups_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "tenancy",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "menu_item_modifier_group_assignments",
                schema: "tenancy",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    menu_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    modifier_group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    menu_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_menu_item_modifier_group_assignments", x => new { x.tenant_id, x.menu_item_id, x.modifier_group_id });
                    table.ForeignKey(
                        name: "fk_item_modifier_assignments_menu_items_item_id",
                        column: x => x.menu_item_id,
                        principalSchema: "tenancy",
                        principalTable: "menu_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_item_modifier_assignments_modifier_groups_group_id",
                        column: x => x.modifier_group_id,
                        principalSchema: "tenancy",
                        principalTable: "modifier_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_item_modifier_assignments_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "tenancy",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "modifier_options",
                schema: "tenancy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    modifier_group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    price_delta_minor_units = table.Column<long>(type: "bigint", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    concurrency_token = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_modifier_options", x => x.id);
                    table.ForeignKey(
                        name: "fk_modifier_options_modifier_groups_group_id",
                        column: x => x.modifier_group_id,
                        principalSchema: "tenancy",
                        principalTable: "modifier_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_modifier_options_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "tenancy",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_menu_item_modifier_group_assignments_menu_item_id",
                schema: "tenancy",
                table: "menu_item_modifier_group_assignments",
                column: "menu_item_id");

            migrationBuilder.CreateIndex(
                name: "IX_menu_item_modifier_group_assignments_modifier_group_id",
                schema: "tenancy",
                table: "menu_item_modifier_group_assignments",
                column: "modifier_group_id");

            migrationBuilder.CreateIndex(
                name: "ix_item_modifier_assignments_tenant_item_sort_order",
                schema: "tenancy",
                table: "menu_item_modifier_group_assignments",
                columns: new[] { "tenant_id", "menu_item_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_modifier_groups_tenant_id_branch_id_sort_order",
                schema: "tenancy",
                table: "modifier_groups",
                columns: new[] { "tenant_id", "branch_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "IX_modifier_options_modifier_group_id",
                schema: "tenancy",
                table: "modifier_options",
                column: "modifier_group_id");

            migrationBuilder.CreateIndex(
                name: "ix_modifier_options_tenant_id_group_id_name",
                schema: "tenancy",
                table: "modifier_options",
                columns: new[] { "tenant_id", "modifier_group_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_modifier_options_tenant_id_group_id_sort_order",
                schema: "tenancy",
                table: "modifier_options",
                columns: new[] { "tenant_id", "modifier_group_id", "sort_order" });

            migrationBuilder.Sql(@"
                ALTER TABLE tenancy.modifier_groups ENABLE ROW LEVEL SECURITY;
                ALTER TABLE tenancy.modifier_groups FORCE ROW LEVEL SECURITY;

                ALTER TABLE tenancy.modifier_options ENABLE ROW LEVEL SECURITY;
                ALTER TABLE tenancy.modifier_options FORCE ROW LEVEL SECURITY;

                ALTER TABLE tenancy.menu_item_modifier_group_assignments ENABLE ROW LEVEL SECURITY;
                ALTER TABLE tenancy.menu_item_modifier_group_assignments FORCE ROW LEVEL SECURITY;

                DROP POLICY IF EXISTS modifier_groups_isolation_policy ON tenancy.modifier_groups;
                CREATE POLICY modifier_groups_isolation_policy ON tenancy.modifier_groups
                    FOR ALL
                    USING (tenant_id = tenancy.get_current_tenant_id())
                    WITH CHECK (tenant_id = tenancy.get_current_tenant_id());

                DROP POLICY IF EXISTS modifier_options_isolation_policy ON tenancy.modifier_options;
                CREATE POLICY modifier_options_isolation_policy ON tenancy.modifier_options
                    FOR ALL
                    USING (tenant_id = tenancy.get_current_tenant_id())
                    WITH CHECK (tenant_id = tenancy.get_current_tenant_id());

                DROP POLICY IF EXISTS menu_item_modifier_group_assignments_isolation_policy ON tenancy.menu_item_modifier_group_assignments;
                CREATE POLICY menu_item_modifier_group_assignments_isolation_policy ON tenancy.menu_item_modifier_group_assignments
                    FOR ALL
                    USING (tenant_id = tenancy.get_current_tenant_id())
                    WITH CHECK (tenant_id = tenancy.get_current_tenant_id());

                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'restaurant_app_runtime') THEN
                        GRANT SELECT, INSERT, UPDATE, DELETE ON tenancy.modifier_groups TO restaurant_app_runtime;
                        GRANT SELECT, INSERT, UPDATE, DELETE ON tenancy.modifier_options TO restaurant_app_runtime;
                        GRANT SELECT, INSERT, UPDATE, DELETE ON tenancy.menu_item_modifier_group_assignments TO restaurant_app_runtime;
                    END IF;
                END $$;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP POLICY IF EXISTS menu_item_modifier_group_assignments_isolation_policy ON tenancy.menu_item_modifier_group_assignments;
                DROP POLICY IF EXISTS modifier_options_isolation_policy ON tenancy.modifier_options;
                DROP POLICY IF EXISTS modifier_groups_isolation_policy ON tenancy.modifier_groups;

                ALTER TABLE tenancy.menu_item_modifier_group_assignments NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE tenancy.menu_item_modifier_group_assignments DISABLE ROW LEVEL SECURITY;

                ALTER TABLE tenancy.modifier_options NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE tenancy.modifier_options DISABLE ROW LEVEL SECURITY;

                ALTER TABLE tenancy.modifier_groups NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE tenancy.modifier_groups DISABLE ROW LEVEL SECURITY;
            ");

            migrationBuilder.DropTable(
                name: "menu_item_modifier_group_assignments",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "modifier_options",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "modifier_groups",
                schema: "tenancy");

            migrationBuilder.DropColumn(
                name: "allergen_tags",
                schema: "tenancy",
                table: "menu_items");

            migrationBuilder.DropColumn(
                name: "dietary_tags",
                schema: "tenancy",
                table: "menu_items");

            migrationBuilder.DropColumn(
                name: "spicy_level",
                schema: "tenancy",
                table: "menu_items");
        }
    }
}
