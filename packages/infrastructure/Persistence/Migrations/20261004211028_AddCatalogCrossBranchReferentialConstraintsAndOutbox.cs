using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantOrder.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalogCrossBranchReferentialConstraintsAndOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM tenancy.menu_items mi
                        JOIN tenancy.menus m ON mi.tenant_id = m.tenant_id AND mi.menu_id = m.id
                        WHERE mi.branch_id <> m.branch_id
                    ) THEN
                        RAISE EXCEPTION 'Pre-migration check failed: Found MenuItem with different branch_id than its Menu.';
                    END IF;

                    IF EXISTS (
                        SELECT 1 FROM tenancy.menu_categories mc
                        JOIN tenancy.menus m ON mc.tenant_id = m.tenant_id AND mc.menu_id = m.id
                        WHERE mc.branch_id <> m.branch_id
                    ) THEN
                        RAISE EXCEPTION 'Pre-migration check failed: Found MenuCategory with different branch_id than its Menu.';
                    END IF;

                    IF EXISTS (
                        SELECT 1 FROM tenancy.item_variants iv
                        JOIN tenancy.menu_items mi ON iv.tenant_id = mi.tenant_id AND iv.menu_item_id = mi.id
                        WHERE iv.branch_id <> mi.branch_id
                    ) THEN
                        RAISE EXCEPTION 'Pre-migration check failed: Found ItemVariant with different branch_id than its MenuItem.';
                    END IF;
                END $$;");

            migrationBuilder.DropForeignKey(
                name: "fk_branch_item_availabilities_item_variants_variant_id",
                schema: "tenancy",
                table: "branch_item_availabilities");

            migrationBuilder.DropForeignKey(
                name: "fk_branch_item_availabilities_menu_items_item_id",
                schema: "tenancy",
                table: "branch_item_availabilities");

            migrationBuilder.DropForeignKey(
                name: "fk_item_variants_menu_items_menu_item_id",
                schema: "tenancy",
                table: "item_variants");

            migrationBuilder.DropForeignKey(
                name: "fk_item_variants_menus_tenant_id_menu_id",
                schema: "tenancy",
                table: "item_variants");

            migrationBuilder.DropForeignKey(
                name: "fk_menu_categories_menus_tenant_id_menu_id",
                schema: "tenancy",
                table: "menu_categories");

            migrationBuilder.DropForeignKey(
                name: "fk_item_modifier_assignments_menu_items_item_id",
                schema: "tenancy",
                table: "menu_item_modifier_group_assignments");

            migrationBuilder.DropForeignKey(
                name: "fk_item_modifier_assignments_modifier_groups_group_id",
                schema: "tenancy",
                table: "menu_item_modifier_group_assignments");

            migrationBuilder.DropForeignKey(
                name: "fk_menu_items_categories_tenant_id_menu_id_category_id",
                schema: "tenancy",
                table: "menu_items");

            migrationBuilder.DropForeignKey(
                name: "fk_menu_items_menus_tenant_id_menu_id",
                schema: "tenancy",
                table: "menu_items");

            migrationBuilder.DropForeignKey(
                name: "fk_menu_items_preparation_stations_station_id",
                schema: "tenancy",
                table: "menu_items");

            migrationBuilder.DropForeignKey(
                name: "fk_modifier_options_modifier_groups_group_id",
                schema: "tenancy",
                table: "modifier_options");

            migrationBuilder.DropIndex(
                name: "IX_modifier_options_modifier_group_id",
                schema: "tenancy",
                table: "modifier_options");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_menus_tenant_id_id",
                schema: "tenancy",
                table: "menus");

            migrationBuilder.DropIndex(
                name: "IX_menu_items_preparation_station_id",
                schema: "tenancy",
                table: "menu_items");

            migrationBuilder.DropIndex(
                name: "IX_menu_items_tenant_id_branch_id",
                schema: "tenancy",
                table: "menu_items");

            migrationBuilder.DropIndex(
                name: "IX_menu_items_tenant_id_menu_id_category_id",
                schema: "tenancy",
                table: "menu_items");

            migrationBuilder.DropIndex(
                name: "IX_menu_item_modifier_group_assignments_menu_item_id",
                schema: "tenancy",
                table: "menu_item_modifier_group_assignments");

            migrationBuilder.DropIndex(
                name: "IX_menu_item_modifier_group_assignments_modifier_group_id",
                schema: "tenancy",
                table: "menu_item_modifier_group_assignments");

            migrationBuilder.DropIndex(
                name: "IX_menu_categories_tenant_id_branch_id",
                schema: "tenancy",
                table: "menu_categories");

            migrationBuilder.DropIndex(
                name: "IX_item_variants_menu_item_id",
                schema: "tenancy",
                table: "item_variants");

            migrationBuilder.DropIndex(
                name: "IX_item_variants_tenant_id_branch_id",
                schema: "tenancy",
                table: "item_variants");

            migrationBuilder.DropIndex(
                name: "IX_item_variants_tenant_id_menu_id",
                schema: "tenancy",
                table: "item_variants");

            migrationBuilder.DropIndex(
                name: "IX_branch_item_availabilities_item_variant_id",
                schema: "tenancy",
                table: "branch_item_availabilities");

            migrationBuilder.DropIndex(
                name: "IX_branch_item_availabilities_menu_item_id",
                schema: "tenancy",
                table: "branch_item_availabilities");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_preparation_stations_tenant_id_branch_id_id",
                schema: "tenancy",
                table: "preparation_stations",
                columns: new[] { "tenant_id", "branch_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_menus_tenant_id_branch_id_id",
                schema: "tenancy",
                table: "menus",
                columns: new[] { "tenant_id", "branch_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_menu_items_tenant_id_branch_id_id",
                schema: "tenancy",
                table: "menu_items",
                columns: new[] { "tenant_id", "branch_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_menu_items_tenant_id_branch_id_menu_id_id",
                schema: "tenancy",
                table: "menu_items",
                columns: new[] { "tenant_id", "branch_id", "menu_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_menu_categories_tenant_id_branch_id_menu_id_id",
                schema: "tenancy",
                table: "menu_categories",
                columns: new[] { "tenant_id", "branch_id", "menu_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_item_variants_tenant_id_branch_id_menu_item_id_id",
                schema: "tenancy",
                table: "item_variants",
                columns: new[] { "tenant_id", "branch_id", "menu_item_id", "id" });

            migrationBuilder.CreateTable(
                name: "catalog_availability_outbox",
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
                    status = table.Column<int>(type: "integer", nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    max_attempts = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    dispatched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_catalog_availability_outbox", x => x.id);
                    table.ForeignKey(
                        name: "fk_catalog_availability_outbox_branches_tenant_id_branch_id",
                        columns: x => new { x.tenant_id, x.branch_id },
                        principalSchema: "tenancy",
                        principalTable: "branches",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_catalog_availability_outbox_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "tenancy",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_modifier_options_tenant_id_branch_id_modifier_group_id",
                schema: "tenancy",
                table: "modifier_options",
                columns: new[] { "tenant_id", "branch_id", "modifier_group_id" });

            migrationBuilder.CreateIndex(
                name: "IX_menu_items_tenant_id_branch_id_menu_id_category_id",
                schema: "tenancy",
                table: "menu_items",
                columns: new[] { "tenant_id", "branch_id", "menu_id", "category_id" });

            migrationBuilder.CreateIndex(
                name: "IX_menu_items_tenant_id_branch_id_preparation_station_id",
                schema: "tenancy",
                table: "menu_items",
                columns: new[] { "tenant_id", "branch_id", "preparation_station_id" });

            migrationBuilder.CreateIndex(
                name: "IX_menu_item_modifier_group_assignments_tenant_id_branch_id_me~",
                schema: "tenancy",
                table: "menu_item_modifier_group_assignments",
                columns: new[] { "tenant_id", "branch_id", "menu_item_id" });

            migrationBuilder.CreateIndex(
                name: "IX_menu_item_modifier_group_assignments_tenant_id_branch_id_mo~",
                schema: "tenancy",
                table: "menu_item_modifier_group_assignments",
                columns: new[] { "tenant_id", "branch_id", "modifier_group_id" });

            migrationBuilder.CreateIndex(
                name: "IX_item_variants_tenant_id_branch_id_menu_id",
                schema: "tenancy",
                table: "item_variants",
                columns: new[] { "tenant_id", "branch_id", "menu_id" });

            migrationBuilder.CreateIndex(
                name: "ix_catalog_availability_outbox_status_next_attempt",
                schema: "tenancy",
                table: "catalog_availability_outbox",
                columns: new[] { "status", "next_attempt_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_catalog_availability_outbox_tenant_id_branch_id",
                schema: "tenancy",
                table: "catalog_availability_outbox",
                columns: new[] { "tenant_id", "branch_id" });

            migrationBuilder.CreateIndex(
                name: "ix_catalog_availability_outbox_tenant_id_idempotency_key",
                schema: "tenancy",
                table: "catalog_availability_outbox",
                columns: new[] { "tenant_id", "idempotency_key" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_branch_item_availabilities_menu_items_tenant_branch_item",
                schema: "tenancy",
                table: "branch_item_availabilities",
                columns: new[] { "tenant_id", "branch_id", "menu_item_id" },
                principalSchema: "tenancy",
                principalTable: "menu_items",
                principalColumns: new[] { "tenant_id", "branch_id", "id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_branch_item_availabilities_variants_tenant_branch_variant",
                schema: "tenancy",
                table: "branch_item_availabilities",
                columns: new[] { "tenant_id", "branch_id", "menu_item_id", "item_variant_id" },
                principalSchema: "tenancy",
                principalTable: "item_variants",
                principalColumns: new[] { "tenant_id", "branch_id", "menu_item_id", "id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_item_variants_menu_items_tenant_branch_item",
                schema: "tenancy",
                table: "item_variants",
                columns: new[] { "tenant_id", "branch_id", "menu_item_id" },
                principalSchema: "tenancy",
                principalTable: "menu_items",
                principalColumns: new[] { "tenant_id", "branch_id", "id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_item_variants_menus_tenant_branch_menu",
                schema: "tenancy",
                table: "item_variants",
                columns: new[] { "tenant_id", "branch_id", "menu_id" },
                principalSchema: "tenancy",
                principalTable: "menus",
                principalColumns: new[] { "tenant_id", "branch_id", "id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_menu_categories_menus_tenant_branch_menu",
                schema: "tenancy",
                table: "menu_categories",
                columns: new[] { "tenant_id", "branch_id", "menu_id" },
                principalSchema: "tenancy",
                principalTable: "menus",
                principalColumns: new[] { "tenant_id", "branch_id", "id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_item_modifier_assignments_groups_tenant_branch_group",
                schema: "tenancy",
                table: "menu_item_modifier_group_assignments",
                columns: new[] { "tenant_id", "branch_id", "modifier_group_id" },
                principalSchema: "tenancy",
                principalTable: "modifier_groups",
                principalColumns: new[] { "tenant_id", "branch_id", "id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_item_modifier_assignments_menu_items_tenant_branch_item",
                schema: "tenancy",
                table: "menu_item_modifier_group_assignments",
                columns: new[] { "tenant_id", "branch_id", "menu_item_id" },
                principalSchema: "tenancy",
                principalTable: "menu_items",
                principalColumns: new[] { "tenant_id", "branch_id", "id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_menu_items_categories_tenant_branch_menu_cat",
                schema: "tenancy",
                table: "menu_items",
                columns: new[] { "tenant_id", "branch_id", "menu_id", "category_id" },
                principalSchema: "tenancy",
                principalTable: "menu_categories",
                principalColumns: new[] { "tenant_id", "branch_id", "menu_id", "id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_menu_items_menus_tenant_branch_menu",
                schema: "tenancy",
                table: "menu_items",
                columns: new[] { "tenant_id", "branch_id", "menu_id" },
                principalSchema: "tenancy",
                principalTable: "menus",
                principalColumns: new[] { "tenant_id", "branch_id", "id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_menu_items_prep_stations_tenant_branch_station",
                schema: "tenancy",
                table: "menu_items",
                columns: new[] { "tenant_id", "branch_id", "preparation_station_id" },
                principalSchema: "tenancy",
                principalTable: "preparation_stations",
                principalColumns: new[] { "tenant_id", "branch_id", "id" },
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_modifier_options_modifier_groups_tenant_branch_group",
                schema: "tenancy",
                table: "modifier_options",
                columns: new[] { "tenant_id", "branch_id", "modifier_group_id" },
                principalSchema: "tenancy",
                principalTable: "modifier_groups",
                principalColumns: new[] { "tenant_id", "branch_id", "id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.Sql(@"
                ALTER TABLE tenancy.catalog_availability_outbox ENABLE ROW LEVEL SECURITY;
                ALTER TABLE tenancy.catalog_availability_outbox FORCE ROW LEVEL SECURITY;

                DROP POLICY IF EXISTS catalog_availability_outbox_isolation_policy ON tenancy.catalog_availability_outbox;
                CREATE POLICY catalog_availability_outbox_isolation_policy ON tenancy.catalog_availability_outbox
                    FOR ALL
                    USING (tenant_id = tenancy.get_current_tenant_id())
                    WITH CHECK (tenant_id = tenancy.get_current_tenant_id());

                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'restaurant_app_runtime') THEN
                        GRANT SELECT, INSERT, UPDATE, DELETE ON tenancy.catalog_availability_outbox TO restaurant_app_runtime;
                    END IF;
                END $$;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP POLICY IF EXISTS catalog_availability_outbox_isolation_policy ON tenancy.catalog_availability_outbox;
                ALTER TABLE tenancy.catalog_availability_outbox NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE tenancy.catalog_availability_outbox DISABLE ROW LEVEL SECURITY;
            ");

            migrationBuilder.DropForeignKey(
                name: "fk_branch_item_availabilities_menu_items_tenant_branch_item",
                schema: "tenancy",
                table: "branch_item_availabilities");

            migrationBuilder.DropForeignKey(
                name: "fk_branch_item_availabilities_variants_tenant_branch_variant",
                schema: "tenancy",
                table: "branch_item_availabilities");

            migrationBuilder.DropForeignKey(
                name: "fk_item_variants_menu_items_tenant_branch_item",
                schema: "tenancy",
                table: "item_variants");

            migrationBuilder.DropForeignKey(
                name: "fk_item_variants_menus_tenant_branch_menu",
                schema: "tenancy",
                table: "item_variants");

            migrationBuilder.DropForeignKey(
                name: "fk_menu_categories_menus_tenant_branch_menu",
                schema: "tenancy",
                table: "menu_categories");

            migrationBuilder.DropForeignKey(
                name: "fk_item_modifier_assignments_groups_tenant_branch_group",
                schema: "tenancy",
                table: "menu_item_modifier_group_assignments");

            migrationBuilder.DropForeignKey(
                name: "fk_item_modifier_assignments_menu_items_tenant_branch_item",
                schema: "tenancy",
                table: "menu_item_modifier_group_assignments");

            migrationBuilder.DropForeignKey(
                name: "fk_menu_items_categories_tenant_branch_menu_cat",
                schema: "tenancy",
                table: "menu_items");

            migrationBuilder.DropForeignKey(
                name: "fk_menu_items_menus_tenant_branch_menu",
                schema: "tenancy",
                table: "menu_items");

            migrationBuilder.DropForeignKey(
                name: "fk_menu_items_prep_stations_tenant_branch_station",
                schema: "tenancy",
                table: "menu_items");

            migrationBuilder.DropForeignKey(
                name: "fk_modifier_options_modifier_groups_tenant_branch_group",
                schema: "tenancy",
                table: "modifier_options");

            migrationBuilder.DropTable(
                name: "catalog_availability_outbox",
                schema: "tenancy");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_preparation_stations_tenant_id_branch_id_id",
                schema: "tenancy",
                table: "preparation_stations");

            migrationBuilder.DropIndex(
                name: "IX_modifier_options_tenant_id_branch_id_modifier_group_id",
                schema: "tenancy",
                table: "modifier_options");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_menus_tenant_id_branch_id_id",
                schema: "tenancy",
                table: "menus");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_menu_items_tenant_id_branch_id_id",
                schema: "tenancy",
                table: "menu_items");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_menu_items_tenant_id_branch_id_menu_id_id",
                schema: "tenancy",
                table: "menu_items");

            migrationBuilder.DropIndex(
                name: "IX_menu_items_tenant_id_branch_id_menu_id_category_id",
                schema: "tenancy",
                table: "menu_items");

            migrationBuilder.DropIndex(
                name: "IX_menu_items_tenant_id_branch_id_preparation_station_id",
                schema: "tenancy",
                table: "menu_items");

            migrationBuilder.DropIndex(
                name: "IX_menu_item_modifier_group_assignments_tenant_id_branch_id_me~",
                schema: "tenancy",
                table: "menu_item_modifier_group_assignments");

            migrationBuilder.DropIndex(
                name: "IX_menu_item_modifier_group_assignments_tenant_id_branch_id_mo~",
                schema: "tenancy",
                table: "menu_item_modifier_group_assignments");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_menu_categories_tenant_id_branch_id_menu_id_id",
                schema: "tenancy",
                table: "menu_categories");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_item_variants_tenant_id_branch_id_menu_item_id_id",
                schema: "tenancy",
                table: "item_variants");

            migrationBuilder.DropIndex(
                name: "IX_item_variants_tenant_id_branch_id_menu_id",
                schema: "tenancy",
                table: "item_variants");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_menus_tenant_id_id",
                schema: "tenancy",
                table: "menus",
                columns: new[] { "tenant_id", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_modifier_options_modifier_group_id",
                schema: "tenancy",
                table: "modifier_options",
                column: "modifier_group_id");

            migrationBuilder.CreateIndex(
                name: "IX_menu_items_preparation_station_id",
                schema: "tenancy",
                table: "menu_items",
                column: "preparation_station_id");

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
                name: "IX_menu_categories_tenant_id_branch_id",
                schema: "tenancy",
                table: "menu_categories",
                columns: new[] { "tenant_id", "branch_id" });

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
                name: "IX_branch_item_availabilities_item_variant_id",
                schema: "tenancy",
                table: "branch_item_availabilities",
                column: "item_variant_id");

            migrationBuilder.CreateIndex(
                name: "IX_branch_item_availabilities_menu_item_id",
                schema: "tenancy",
                table: "branch_item_availabilities",
                column: "menu_item_id");

            migrationBuilder.AddForeignKey(
                name: "fk_branch_item_availabilities_item_variants_variant_id",
                schema: "tenancy",
                table: "branch_item_availabilities",
                column: "item_variant_id",
                principalSchema: "tenancy",
                principalTable: "item_variants",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_branch_item_availabilities_menu_items_item_id",
                schema: "tenancy",
                table: "branch_item_availabilities",
                column: "menu_item_id",
                principalSchema: "tenancy",
                principalTable: "menu_items",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_item_variants_menu_items_menu_item_id",
                schema: "tenancy",
                table: "item_variants",
                column: "menu_item_id",
                principalSchema: "tenancy",
                principalTable: "menu_items",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_item_variants_menus_tenant_id_menu_id",
                schema: "tenancy",
                table: "item_variants",
                columns: new[] { "tenant_id", "menu_id" },
                principalSchema: "tenancy",
                principalTable: "menus",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_menu_categories_menus_tenant_id_menu_id",
                schema: "tenancy",
                table: "menu_categories",
                columns: new[] { "tenant_id", "menu_id" },
                principalSchema: "tenancy",
                principalTable: "menus",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_item_modifier_assignments_menu_items_item_id",
                schema: "tenancy",
                table: "menu_item_modifier_group_assignments",
                column: "menu_item_id",
                principalSchema: "tenancy",
                principalTable: "menu_items",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_item_modifier_assignments_modifier_groups_group_id",
                schema: "tenancy",
                table: "menu_item_modifier_group_assignments",
                column: "modifier_group_id",
                principalSchema: "tenancy",
                principalTable: "modifier_groups",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_menu_items_categories_tenant_id_menu_id_category_id",
                schema: "tenancy",
                table: "menu_items",
                columns: new[] { "tenant_id", "menu_id", "category_id" },
                principalSchema: "tenancy",
                principalTable: "menu_categories",
                principalColumns: new[] { "tenant_id", "menu_id", "id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_menu_items_menus_tenant_id_menu_id",
                schema: "tenancy",
                table: "menu_items",
                columns: new[] { "tenant_id", "menu_id" },
                principalSchema: "tenancy",
                principalTable: "menus",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_menu_items_preparation_stations_station_id",
                schema: "tenancy",
                table: "menu_items",
                column: "preparation_station_id",
                principalSchema: "tenancy",
                principalTable: "preparation_stations",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_modifier_options_modifier_groups_group_id",
                schema: "tenancy",
                table: "modifier_options",
                column: "modifier_group_id",
                principalSchema: "tenancy",
                principalTable: "modifier_groups",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
