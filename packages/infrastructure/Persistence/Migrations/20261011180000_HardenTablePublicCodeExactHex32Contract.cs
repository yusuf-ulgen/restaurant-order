using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantOrder.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HardenTablePublicCodeExactHex32Contract : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_restaurant_tables_public_code",
                schema: "tenancy",
                table: "restaurant_tables");

            // Regenerate only non-compliant codes (not 32-char lowercase hex) and bump qr_version
            migrationBuilder.Sql(@"
                UPDATE tenancy.restaurant_tables
                SET public_code = encode(gen_random_bytes(16), 'hex'),
                    qr_version = qr_version + 1,
                    updated_at = clock_timestamp()
                WHERE public_code IS NULL OR length(public_code) <> 32 OR public_code !~ '^[0-9a-f]{32}$';
            ");

            migrationBuilder.AddCheckConstraint(
                name: "ck_restaurant_tables_public_code",
                schema: "tenancy",
                table: "restaurant_tables",
                sql: "length(public_code) = 32 AND public_code ~ '^[0-9a-f]{32}$'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_restaurant_tables_public_code",
                schema: "tenancy",
                table: "restaurant_tables");

            migrationBuilder.AddCheckConstraint(
                name: "ck_restaurant_tables_public_code",
                schema: "tenancy",
                table: "restaurant_tables",
                sql: "length(public_code) >= 16 AND length(public_code) <= 64 AND public_code ~ '^[0-9a-zA-Z_-]+$'");
        }
    }
}
