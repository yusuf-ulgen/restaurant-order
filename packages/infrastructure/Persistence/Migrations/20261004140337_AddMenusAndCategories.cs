using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantOrder.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMenusAndCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "menus",
                schema: "tenancy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    slug = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    concurrency_token = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_menus", x => x.id);
                    table.UniqueConstraint("AK_menus_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_menus_branches_tenant_id_branch_id",
                        columns: x => new { x.tenant_id, x.branch_id },
                        principalSchema: "tenancy",
                        principalTable: "branches",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_menus_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "tenancy",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "menu_categories",
                schema: "tenancy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    menu_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    slug = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    concurrency_token = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_menu_categories", x => x.id);
                    table.ForeignKey(
                        name: "fk_menu_categories_branches_tenant_id_branch_id",
                        columns: x => new { x.tenant_id, x.branch_id },
                        principalSchema: "tenancy",
                        principalTable: "branches",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_menu_categories_menus_tenant_id_menu_id",
                        columns: x => new { x.tenant_id, x.menu_id },
                        principalSchema: "tenancy",
                        principalTable: "menus",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_menu_categories_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "tenancy",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_menu_categories_tenant_id_branch_id",
                schema: "tenancy",
                table: "menu_categories",
                columns: new[] { "tenant_id", "branch_id" });

            migrationBuilder.CreateIndex(
                name: "ix_menu_categories_tenant_id_menu_id_slug",
                schema: "tenancy",
                table: "menu_categories",
                columns: new[] { "tenant_id", "menu_id", "slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_menu_categories_tenant_id_menu_id_sort_order",
                schema: "tenancy",
                table: "menu_categories",
                columns: new[] { "tenant_id", "menu_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_menus_tenant_id_branch_id_slug",
                schema: "tenancy",
                table: "menus",
                columns: new[] { "tenant_id", "branch_id", "slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_menus_tenant_id_branch_id_sort_order",
                schema: "tenancy",
                table: "menus",
                columns: new[] { "tenant_id", "branch_id", "sort_order" });

            migrationBuilder.Sql(@"
                ALTER TABLE tenancy.menus ENABLE ROW LEVEL SECURITY;
                ALTER TABLE tenancy.menus FORCE ROW LEVEL SECURITY;

                ALTER TABLE tenancy.menu_categories ENABLE ROW LEVEL SECURITY;
                ALTER TABLE tenancy.menu_categories FORCE ROW LEVEL SECURITY;

                DROP POLICY IF EXISTS menus_isolation_policy ON tenancy.menus;
                CREATE POLICY menus_isolation_policy ON tenancy.menus
                    FOR ALL
                    USING (tenant_id = tenancy.get_current_tenant_id())
                    WITH CHECK (tenant_id = tenancy.get_current_tenant_id());

                DROP POLICY IF EXISTS menu_categories_isolation_policy ON tenancy.menu_categories;
                CREATE POLICY menu_categories_isolation_policy ON tenancy.menu_categories
                    FOR ALL
                    USING (tenant_id = tenancy.get_current_tenant_id())
                    WITH CHECK (tenant_id = tenancy.get_current_tenant_id());

                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'restaurant_app_runtime') THEN
                        GRANT SELECT, INSERT, UPDATE, DELETE ON tenancy.menus TO restaurant_app_runtime;
                        GRANT SELECT, INSERT, UPDATE, DELETE ON tenancy.menu_categories TO restaurant_app_runtime;
                    END IF;
                END $$;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP POLICY IF EXISTS menu_categories_isolation_policy ON tenancy.menu_categories;
                DROP POLICY IF EXISTS menus_isolation_policy ON tenancy.menus;

                ALTER TABLE tenancy.menu_categories NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE tenancy.menu_categories DISABLE ROW LEVEL SECURITY;

                ALTER TABLE tenancy.menus NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE tenancy.menus DISABLE ROW LEVEL SECURITY;
            ");

            migrationBuilder.DropTable(
                name: "menu_categories",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "menus",
                schema: "tenancy");
        }
    }
}
