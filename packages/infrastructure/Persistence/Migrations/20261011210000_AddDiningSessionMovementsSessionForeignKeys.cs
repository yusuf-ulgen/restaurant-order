using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantOrder.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDiningSessionMovementsSessionForeignKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_dining_session_movements_tenant_branch_target_session",
                schema: "tenancy",
                table: "dining_session_movements",
                columns: new[] { "tenant_id", "branch_id", "target_session_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_dining_session_movements_source_session",
                schema: "tenancy",
                table: "dining_session_movements",
                columns: new[] { "tenant_id", "branch_id", "source_session_id" },
                principalSchema: "tenancy",
                principalTable: "dining_sessions",
                principalColumns: new[] { "tenant_id", "branch_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_dining_session_movements_target_session",
                schema: "tenancy",
                table: "dining_session_movements",
                columns: new[] { "tenant_id", "branch_id", "target_session_id" },
                principalSchema: "tenancy",
                principalTable: "dining_sessions",
                principalColumns: new[] { "tenant_id", "branch_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_dining_session_movements_source_session",
                schema: "tenancy",
                table: "dining_session_movements");

            migrationBuilder.DropForeignKey(
                name: "fk_dining_session_movements_target_session",
                schema: "tenancy",
                table: "dining_session_movements");

            migrationBuilder.DropIndex(
                name: "ix_dining_session_movements_tenant_branch_target_session",
                schema: "tenancy",
                table: "dining_session_movements");
        }
    }
}
