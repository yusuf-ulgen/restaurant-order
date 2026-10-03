using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantOrder.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDiningAreasStationsAndFeatureFlags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "branch_feature_flags",
                schema: "tenancy",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    overrides_json = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    concurrency_token = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_branch_feature_flags", x => new { x.tenant_id, x.branch_id });
                    table.ForeignKey(
                        name: "fk_branch_feature_flags_branches_tenant_id_branch_id",
                        columns: x => new { x.tenant_id, x.branch_id },
                        principalSchema: "tenancy",
                        principalTable: "branches",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_branch_feature_flags_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "tenancy",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "dining_areas",
                schema: "tenancy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    area_type = table.Column<int>(type: "integer", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    concurrency_token = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dining_areas", x => x.id);
                    table.ForeignKey(
                        name: "fk_dining_areas_branches_tenant_id_branch_id",
                        columns: x => new { x.tenant_id, x.branch_id },
                        principalSchema: "tenancy",
                        principalTable: "branches",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_dining_areas_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "tenancy",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "preparation_stations",
                schema: "tenancy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    display_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    station_type = table.Column<int>(type: "integer", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    concurrency_token = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_preparation_stations", x => x.id);
                    table.ForeignKey(
                        name: "fk_preparation_stations_branches_tenant_id_branch_id",
                        columns: x => new { x.tenant_id, x.branch_id },
                        principalSchema: "tenancy",
                        principalTable: "branches",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_preparation_stations_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "tenancy",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tenant_feature_flags",
                schema: "tenancy",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    flags_json = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    concurrency_token = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenant_feature_flags", x => x.tenant_id);
                    table.ForeignKey(
                        name: "fk_tenant_feature_flags_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "tenancy",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_dining_areas_tenant_id_branch_id_code",
                schema: "tenancy",
                table: "dining_areas",
                columns: new[] { "tenant_id", "branch_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_dining_areas_tenant_id_branch_id_sort_order",
                schema: "tenancy",
                table: "dining_areas",
                columns: new[] { "tenant_id", "branch_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_preparation_stations_tenant_id_branch_id_code",
                schema: "tenancy",
                table: "preparation_stations",
                columns: new[] { "tenant_id", "branch_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_preparation_stations_tenant_id_branch_id_sort_order",
                schema: "tenancy",
                table: "preparation_stations",
                columns: new[] { "tenant_id", "branch_id", "sort_order" });

            // Enable and FORCE RLS on all 4 tables
            migrationBuilder.Sql(@"
                ALTER TABLE tenancy.dining_areas ENABLE ROW LEVEL SECURITY;
                ALTER TABLE tenancy.dining_areas FORCE ROW LEVEL SECURITY;

                ALTER TABLE tenancy.preparation_stations ENABLE ROW LEVEL SECURITY;
                ALTER TABLE tenancy.preparation_stations FORCE ROW LEVEL SECURITY;

                ALTER TABLE tenancy.tenant_feature_flags ENABLE ROW LEVEL SECURITY;
                ALTER TABLE tenancy.tenant_feature_flags FORCE ROW LEVEL SECURITY;

                ALTER TABLE tenancy.branch_feature_flags ENABLE ROW LEVEL SECURITY;
                ALTER TABLE tenancy.branch_feature_flags FORCE ROW LEVEL SECURITY;

                DROP POLICY IF EXISTS dining_areas_isolation_policy ON tenancy.dining_areas;
                CREATE POLICY dining_areas_isolation_policy ON tenancy.dining_areas
                    FOR ALL
                    USING (tenant_id = tenancy.get_current_tenant_id())
                    WITH CHECK (tenant_id = tenancy.get_current_tenant_id());

                DROP POLICY IF EXISTS preparation_stations_isolation_policy ON tenancy.preparation_stations;
                CREATE POLICY preparation_stations_isolation_policy ON tenancy.preparation_stations
                    FOR ALL
                    USING (tenant_id = tenancy.get_current_tenant_id())
                    WITH CHECK (tenant_id = tenancy.get_current_tenant_id());

                DROP POLICY IF EXISTS tenant_feature_flags_isolation_policy ON tenancy.tenant_feature_flags;
                CREATE POLICY tenant_feature_flags_isolation_policy ON tenancy.tenant_feature_flags
                    FOR ALL
                    USING (tenant_id = tenancy.get_current_tenant_id())
                    WITH CHECK (tenant_id = tenancy.get_current_tenant_id());

                DROP POLICY IF EXISTS branch_feature_flags_isolation_policy ON tenancy.branch_feature_flags;
                CREATE POLICY branch_feature_flags_isolation_policy ON tenancy.branch_feature_flags
                    FOR ALL
                    USING (tenant_id = tenancy.get_current_tenant_id())
                    WITH CHECK (tenant_id = tenancy.get_current_tenant_id());

                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'restaurant_app_runtime') THEN
                        GRANT SELECT, INSERT, UPDATE, DELETE ON tenancy.dining_areas TO restaurant_app_runtime;
                        GRANT SELECT, INSERT, UPDATE, DELETE ON tenancy.preparation_stations TO restaurant_app_runtime;
                        GRANT SELECT, INSERT, UPDATE, DELETE ON tenancy.tenant_feature_flags TO restaurant_app_runtime;
                        GRANT SELECT, INSERT, UPDATE, DELETE ON tenancy.branch_feature_flags TO restaurant_app_runtime;
                    END IF;
                END $$;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP POLICY IF EXISTS branch_feature_flags_isolation_policy ON tenancy.branch_feature_flags;
                DROP POLICY IF EXISTS tenant_feature_flags_isolation_policy ON tenancy.tenant_feature_flags;
                DROP POLICY IF EXISTS preparation_stations_isolation_policy ON tenancy.preparation_stations;
                DROP POLICY IF EXISTS dining_areas_isolation_policy ON tenancy.dining_areas;

                ALTER TABLE tenancy.branch_feature_flags NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE tenancy.branch_feature_flags DISABLE ROW LEVEL SECURITY;

                ALTER TABLE tenancy.tenant_feature_flags NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE tenancy.tenant_feature_flags DISABLE ROW LEVEL SECURITY;

                ALTER TABLE tenancy.preparation_stations NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE tenancy.preparation_stations DISABLE ROW LEVEL SECURITY;

                ALTER TABLE tenancy.dining_areas NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE tenancy.dining_areas DISABLE ROW LEVEL SECURITY;
            ");

            migrationBuilder.DropTable(
                name: "branch_feature_flags",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "dining_areas",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "preparation_stations",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "tenant_feature_flags",
                schema: "tenancy");
        }
    }
}
