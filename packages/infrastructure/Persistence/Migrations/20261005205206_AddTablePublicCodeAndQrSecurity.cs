using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantOrder.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTablePublicCodeAndQrSecurity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "public_code",
                schema: "tenancy",
                table: "restaurant_tables",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql(
                "UPDATE tenancy.restaurant_tables SET public_code = substr(md5(random()::text || clock_timestamp()::text || id::text), 1, 32) WHERE public_code = '';");

            migrationBuilder.CreateIndex(
                name: "ix_restaurant_tables_tenant_branch_public_code",
                schema: "tenancy",
                table: "restaurant_tables",
                columns: new[] { "tenant_id", "branch_id", "public_code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_restaurant_tables_tenant_branch_public_code",
                schema: "tenancy",
                table: "restaurant_tables");

            migrationBuilder.DropColumn(
                name: "public_code",
                schema: "tenancy",
                table: "restaurant_tables");
        }
    }
}
