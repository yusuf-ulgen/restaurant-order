using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantOrder.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBrandAppearanceAndBranchThemeOverrides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "brand_appearances",
                schema: "tenancy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    brand_id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    logo_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    favicon_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    primary_color = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    primary_hover_color = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    secondary_color = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    accent_color = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    surface_color = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    background_color = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    footer_text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    default_shell_title = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    default_shell_subtitle = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    concurrency_token = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_brand_appearances", x => x.id);
                    table.ForeignKey(
                        name: "fk_brand_appearances_brands_tenant_id_brand_id",
                        columns: x => new { x.tenant_id, x.brand_id },
                        principalSchema: "tenancy",
                        principalTable: "brands",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_brand_appearances_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "tenancy",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "branch_theme_overrides",
                schema: "tenancy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    logo_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    header_subtitle = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    footer_branch_info = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    concurrency_token = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_branch_theme_overrides", x => x.id);
                    table.ForeignKey(
                        name: "fk_branch_theme_overrides_branches_tenant_id_branch_id",
                        columns: x => new { x.tenant_id, x.branch_id },
                        principalSchema: "tenancy",
                        principalTable: "branches",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_branch_theme_overrides_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "tenancy",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_brand_appearances_tenant_id_brand_id",
                schema: "tenancy",
                table: "brand_appearances",
                columns: new[] { "tenant_id", "brand_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_branch_theme_overrides_tenant_id_branch_id",
                schema: "tenancy",
                table: "branch_theme_overrides",
                columns: new[] { "tenant_id", "branch_id" },
                unique: true);

            // Enable and FORCE RLS
            migrationBuilder.Sql(@"
                ALTER TABLE tenancy.brand_appearances ENABLE ROW LEVEL SECURITY;
                ALTER TABLE tenancy.brand_appearances FORCE ROW LEVEL SECURITY;

                ALTER TABLE tenancy.branch_theme_overrides ENABLE ROW LEVEL SECURITY;
                ALTER TABLE tenancy.branch_theme_overrides FORCE ROW LEVEL SECURITY;

                DROP POLICY IF EXISTS brand_appearance_isolation_policy ON tenancy.brand_appearances;
                CREATE POLICY brand_appearance_isolation_policy ON tenancy.brand_appearances
                    FOR ALL
                    USING (tenant_id = tenancy.get_current_tenant_id())
                    WITH CHECK (tenant_id = tenancy.get_current_tenant_id());

                DROP POLICY IF EXISTS branch_theme_override_isolation_policy ON tenancy.branch_theme_overrides;
                CREATE POLICY branch_theme_override_isolation_policy ON tenancy.branch_theme_overrides
                    FOR ALL
                    USING (tenant_id = tenancy.get_current_tenant_id())
                    WITH CHECK (tenant_id = tenancy.get_current_tenant_id());

                GRANT SELECT, INSERT, UPDATE, DELETE ON tenancy.brand_appearances TO restaurant_app_runtime;
                GRANT SELECT, INSERT, UPDATE, DELETE ON tenancy.branch_theme_overrides TO restaurant_app_runtime;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP POLICY IF EXISTS branch_theme_override_isolation_policy ON tenancy.branch_theme_overrides;
                DROP POLICY IF EXISTS brand_appearance_isolation_policy ON tenancy.brand_appearances;

                ALTER TABLE tenancy.branch_theme_overrides NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE tenancy.branch_theme_overrides DISABLE ROW LEVEL SECURITY;

                ALTER TABLE tenancy.brand_appearances NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE tenancy.brand_appearances DISABLE ROW LEVEL SECURITY;
            ");

            migrationBuilder.DropTable(
                name: "branch_theme_overrides",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "brand_appearances",
                schema: "tenancy");
        }
    }
}
