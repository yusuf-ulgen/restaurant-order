using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantOrder.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HardenCatalogOutboxAndPreparationStationConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_menu_items_prep_stations_tenant_branch_station",
                schema: "tenancy",
                table: "menu_items");

            migrationBuilder.AddForeignKey(
                name: "fk_menu_items_prep_stations_tenant_branch_station",
                schema: "tenancy",
                table: "menu_items",
                columns: new[] { "tenant_id", "branch_id", "preparation_station_id" },
                principalSchema: "tenancy",
                principalTable: "preparation_stations",
                principalColumns: new[] { "tenant_id", "branch_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_menu_items_prep_stations_tenant_branch_station",
                schema: "tenancy",
                table: "menu_items");

            migrationBuilder.AddForeignKey(
                name: "fk_menu_items_prep_stations_tenant_branch_station",
                schema: "tenancy",
                table: "menu_items",
                columns: new[] { "tenant_id", "branch_id", "preparation_station_id" },
                principalSchema: "tenancy",
                principalTable: "preparation_stations",
                principalColumns: new[] { "tenant_id", "branch_id", "id" },
                onDelete: ReferentialAction.SetNull);
        }
    }
}
