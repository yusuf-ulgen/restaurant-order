using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantOrder.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HardenTableAndDiningSessionIntegrityConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_dining_sessions_status_timestamps",
                schema: "tenancy",
                table: "dining_sessions");

            migrationBuilder.AddCheckConstraint(
                name: "ck_restaurant_tables_canvas_bounds",
                schema: "tenancy",
                table: "restaurant_tables",
                sql: "position_x + width <= 10000 AND position_y + height <= 10000");

            migrationBuilder.AddCheckConstraint(
                name: "ck_restaurant_tables_shape",
                schema: "tenancy",
                table: "restaurant_tables",
                sql: "shape IN (1, 2, 3)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_dining_sessions_chronology",
                schema: "tenancy",
                table: "dining_sessions",
                sql: "((activated_at_utc IS NULL OR opened_at_utc <= activated_at_utc) AND (bill_requested_at_utc IS NULL OR (activated_at_utc IS NOT NULL AND activated_at_utc <= bill_requested_at_utc)) AND (closed_at_utc IS NULL OR (    (merged_into_session_id IS NULL AND bill_requested_at_utc IS NOT NULL AND bill_requested_at_utc <= closed_at_utc)     OR (merged_into_session_id IS NOT NULL AND (        (activated_at_utc IS NOT NULL AND activated_at_utc <= closed_at_utc)         OR (activated_at_utc IS NULL AND opened_at_utc <= closed_at_utc)    )))))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_dining_sessions_status_timestamps",
                schema: "tenancy",
                table: "dining_sessions",
                sql: "((status = 1 AND activated_at_utc IS NULL AND bill_requested_at_utc IS NULL AND closed_at_utc IS NULL AND merged_into_session_id IS NULL) OR (status = 2 AND activated_at_utc IS NOT NULL AND bill_requested_at_utc IS NULL AND closed_at_utc IS NULL AND merged_into_session_id IS NULL) OR (status = 3 AND activated_at_utc IS NOT NULL AND bill_requested_at_utc IS NOT NULL AND closed_at_utc IS NULL AND merged_into_session_id IS NULL) OR (status = 4 AND merged_into_session_id IS NULL AND activated_at_utc IS NOT NULL AND bill_requested_at_utc IS NOT NULL AND closed_at_utc IS NOT NULL) OR (status = 4 AND merged_into_session_id IS NOT NULL AND bill_requested_at_utc IS NULL AND closed_at_utc IS NOT NULL))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_restaurant_tables_canvas_bounds",
                schema: "tenancy",
                table: "restaurant_tables");

            migrationBuilder.DropCheckConstraint(
                name: "ck_restaurant_tables_shape",
                schema: "tenancy",
                table: "restaurant_tables");

            migrationBuilder.DropCheckConstraint(
                name: "ck_dining_sessions_chronology",
                schema: "tenancy",
                table: "dining_sessions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_dining_sessions_status_timestamps",
                schema: "tenancy",
                table: "dining_sessions");

            migrationBuilder.AddCheckConstraint(
                name: "ck_dining_sessions_status_timestamps",
                schema: "tenancy",
                table: "dining_sessions",
                sql: "((status = 1 AND activated_at_utc IS NULL AND bill_requested_at_utc IS NULL AND closed_at_utc IS NULL) OR (status = 2 AND activated_at_utc IS NOT NULL AND closed_at_utc IS NULL) OR (status = 3 AND activated_at_utc IS NOT NULL AND bill_requested_at_utc IS NOT NULL AND closed_at_utc IS NULL) OR (status = 4 AND closed_at_utc IS NOT NULL))");
        }
    }
}
