using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantOrder.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HardenTablePublicCodeCsprngAndConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Ensure cryptographic random extension pgcrypto is available
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pgcrypto;");

            // 2. Remove the empty string default value constraint from public_code column
            migrationBuilder.AlterColumn<string>(
                name: "public_code",
                schema: "tenancy",
                table: "restaurant_tables",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldDefaultValue: "");

            // 3. Safely regenerate any weak (md5/empty) public codes using PostgreSQL CSPRNG (gen_random_bytes)
            // and bump qr_version to invalidate any previously printed/distributed weak QR tokens
            migrationBuilder.Sql(@"
                UPDATE tenancy.restaurant_tables
                SET public_code = encode(gen_random_bytes(16), 'hex'),
                    qr_version = qr_version + 1,
                    updated_at = clock_timestamp();
            ");

            // 4. Ensure NOT NULL and add check constraint for length and format
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM pg_constraint
                        WHERE conname = 'ck_restaurant_tables_public_code'
                    ) THEN
                        ALTER TABLE tenancy.restaurant_tables
                        ADD CONSTRAINT ck_restaurant_tables_public_code
                        CHECK (length(public_code) >= 16 AND length(public_code) <= 64 AND public_code ~ '^[0-9a-zA-Z_-]+$');
                    END IF;
                END $$;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE tenancy.restaurant_tables DROP CONSTRAINT IF EXISTS ck_restaurant_tables_public_code;");

            migrationBuilder.AlterColumn<string>(
                name: "public_code",
                schema: "tenancy",
                table: "restaurant_tables",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);
        }
    }
}
