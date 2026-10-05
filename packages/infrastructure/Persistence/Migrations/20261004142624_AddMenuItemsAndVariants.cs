using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantOrder.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMenuItemsAndVariants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_menu_categories_tenant_id_menu_id_id",
                schema: "tenancy",
                table: "menu_categories",
                columns: new[] { "tenant_id", "menu_id", "id" });

            migrationBuilder.CreateTable(
                name: "menu_items",
                schema: "tenancy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    menu_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    slug = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    short_description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    full_description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    image_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    base_price_minor_units = table.Column<long>(type: "bigint", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    concurrency_token = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_menu_items", x => x.id);
                    table.UniqueConstraint("AK_menu_items_tenant_id_menu_id_id", x => new { x.tenant_id, x.menu_id, x.id });
                    table.ForeignKey(
                        name: "fk_menu_items_branches_tenant_id_branch_id",
                        columns: x => new { x.tenant_id, x.branch_id },
                        principalSchema: "tenancy",
                        principalTable: "branches",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_menu_items_categories_tenant_id_menu_id_category_id",
                        columns: x => new { x.tenant_id, x.menu_id, x.category_id },
                        principalSchema: "tenancy",
                        principalTable: "menu_categories",
                        principalColumns: new[] { "tenant_id", "menu_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_menu_items_menus_tenant_id_menu_id",
                        columns: x => new { x.tenant_id, x.menu_id },
                        principalSchema: "tenancy",
                        principalTable: "menus",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_menu_items_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "tenancy",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "item_variants",
                schema: "tenancy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    menu_id = table.Column<Guid>(type: "uuid", nullable: false),
                    menu_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    absolute_price_minor_units = table.Column<long>(type: "bigint", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    concurrency_token = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_item_variants", x => x.id);
                    table.ForeignKey(
                        name: "fk_item_variants_branches_tenant_id_branch_id",
                        columns: x => new { x.tenant_id, x.branch_id },
                        principalSchema: "tenancy",
                        principalTable: "branches",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_item_variants_menu_items_menu_item_id",
                        column: x => x.menu_item_id,
                        principalSchema: "tenancy",
                        principalTable: "menu_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_item_variants_menus_tenant_id_menu_id",
                        columns: x => new { x.tenant_id, x.menu_id },
                        principalSchema: "tenancy",
                        principalTable: "menus",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_item_variants_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "tenancy",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_item_variants_menu_item_id",
                schema: "tenancy",
                table: "item_variants",
                column: "menu_item_id");

            migrationBuilder.CreateIndex(
                name: "IX_item_variants_tenant_id_branch_id",
                schema: "tenancy",
                table: "item_variants",
                columns: new[] { "tenant_id", "branch_id" });

            migrationBuilder.CreateIndex(
                name: "IX_item_variants_tenant_id_menu_id",
                schema: "tenancy",
                table: "item_variants",
                columns: new[] { "tenant_id", "menu_id" });

            migrationBuilder.CreateIndex(
                name: "ix_item_variants_single_active_default",
                schema: "tenancy",
                table: "item_variants",
                columns: new[] { "tenant_id", "menu_item_id" },
                unique: true,
                filter: "is_default = true AND is_active = true");

            migrationBuilder.CreateIndex(
                name: "ix_item_variants_tenant_id_menu_item_id_code",
                schema: "tenancy",
                table: "item_variants",
                columns: new[] { "tenant_id", "menu_item_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_item_variants_tenant_id_menu_item_id_sort_order",
                schema: "tenancy",
                table: "item_variants",
                columns: new[] { "tenant_id", "menu_item_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "IX_menu_items_tenant_id_branch_id",
                schema: "tenancy",
                table: "menu_items",
                columns: new[] { "tenant_id", "branch_id" });

            migrationBuilder.CreateIndex(
                name: "IX_menu_items_tenant_id_menu_id_category_id",
                schema: "tenancy",
                table: "menu_items",
                columns: new[] { "tenant_id", "menu_id", "category_id" });

            migrationBuilder.CreateIndex(
                name: "ix_menu_items_tenant_id_category_id_sort_order",
                schema: "tenancy",
                table: "menu_items",
                columns: new[] { "tenant_id", "category_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_menu_items_tenant_id_menu_id_slug",
                schema: "tenancy",
                table: "menu_items",
                columns: new[] { "tenant_id", "menu_id", "slug" },
                unique: true);

            migrationBuilder.Sql(@"
                ALTER TABLE tenancy.menu_items ENABLE ROW LEVEL SECURITY;
                ALTER TABLE tenancy.menu_items FORCE ROW LEVEL SECURITY;

                ALTER TABLE tenancy.item_variants ENABLE ROW LEVEL SECURITY;
                ALTER TABLE tenancy.item_variants FORCE ROW LEVEL SECURITY;

                DROP POLICY IF EXISTS menu_items_isolation_policy ON tenancy.menu_items;
                CREATE POLICY menu_items_isolation_policy ON tenancy.menu_items
                    FOR ALL
                    USING (tenant_id = tenancy.get_current_tenant_id())
                    WITH CHECK (tenant_id = tenancy.get_current_tenant_id());

                DROP POLICY IF EXISTS item_variants_isolation_policy ON tenancy.item_variants;
                CREATE POLICY item_variants_isolation_policy ON tenancy.item_variants
                    FOR ALL
                    USING (tenant_id = tenancy.get_current_tenant_id())
                    WITH CHECK (tenant_id = tenancy.get_current_tenant_id());

                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'restaurant_app_runtime') THEN
                        GRANT SELECT, INSERT, UPDATE, DELETE ON tenancy.menu_items TO restaurant_app_runtime;
                        GRANT SELECT, INSERT, UPDATE, DELETE ON tenancy.item_variants TO restaurant_app_runtime;
                    END IF;
                END $$;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP POLICY IF EXISTS item_variants_isolation_policy ON tenancy.item_variants;
                DROP POLICY IF EXISTS menu_items_isolation_policy ON tenancy.menu_items;

                ALTER TABLE tenancy.item_variants NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE tenancy.item_variants DISABLE ROW LEVEL SECURITY;

                ALTER TABLE tenancy.menu_items NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE tenancy.menu_items DISABLE ROW LEVEL SECURITY;
            ");

            migrationBuilder.DropTable(
                name: "item_variants",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "menu_items",
                schema: "tenancy");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_menu_categories_tenant_id_menu_id_id",
                schema: "tenancy",
                table: "menu_categories");
        }
    }
}
