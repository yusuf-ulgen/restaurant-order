using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantOrder.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDiningSessionMovementsAndMechanics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "dining_session_movements",
                schema: "tenancy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_table_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    target_table_id = table.Column<Guid>(type: "uuid", nullable: false),
                    movement_type = table.Column<int>(type: "integer", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_role = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    idempotency_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    guests_moved = table.Column<int>(type: "integer", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dining_session_movements", x => x.id);
                    table.CheckConstraint("ck_dining_session_movements_guests", "guests_moved >= 1");
                    table.CheckConstraint("ck_dining_session_movements_tables_diff", "(movement_type = 1 AND source_table_id <> target_table_id) OR (movement_type = 2)");
                    table.CheckConstraint("ck_dining_session_movements_type", "movement_type IN (1, 2)");
                    table.ForeignKey(
                        name: "fk_dining_session_movements_source_table",
                        columns: x => new { x.tenant_id, x.branch_id, x.source_table_id },
                        principalSchema: "tenancy",
                        principalTable: "restaurant_tables",
                        principalColumns: new[] { "tenant_id", "branch_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_dining_session_movements_target_table",
                        columns: x => new { x.tenant_id, x.branch_id, x.target_table_id },
                        principalSchema: "tenancy",
                        principalTable: "restaurant_tables",
                        principalColumns: new[] { "tenant_id", "branch_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_dining_session_movements_tenant_branch_created",
                schema: "tenancy",
                table: "dining_session_movements",
                columns: new[] { "tenant_id", "branch_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_dining_session_movements_tenant_branch_idempotency_key",
                schema: "tenancy",
                table: "dining_session_movements",
                columns: new[] { "tenant_id", "branch_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_dining_session_movements_tenant_branch_source_session",
                schema: "tenancy",
                table: "dining_session_movements",
                columns: new[] { "tenant_id", "branch_id", "source_session_id" });

            migrationBuilder.Sql(@"
                ALTER TABLE tenancy.dining_session_movements ENABLE ROW LEVEL SECURITY;
                ALTER TABLE tenancy.dining_session_movements FORCE ROW LEVEL SECURITY;

                DROP POLICY IF EXISTS dining_session_movements_isolation_policy ON tenancy.dining_session_movements;
                CREATE POLICY dining_session_movements_isolation_policy ON tenancy.dining_session_movements
                    FOR ALL
                    USING (tenant_id = tenancy.get_current_tenant_id())
                    WITH CHECK (tenant_id = tenancy.get_current_tenant_id());

                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'restaurant_app_runtime') THEN
                        GRANT SELECT, INSERT ON tenancy.dining_session_movements TO restaurant_app_runtime;
                    END IF;
                END $$;

                CREATE OR REPLACE FUNCTION tenancy.prevent_dining_session_movements_mutation()
                RETURNS trigger AS $$
                BEGIN
                    RAISE EXCEPTION 'Dining session movements are immutable and cannot be updated or deleted.';
                END;
                $$ LANGUAGE plpgsql;

                DROP TRIGGER IF EXISTS trg_dining_session_movements_immutable ON tenancy.dining_session_movements;
                CREATE TRIGGER trg_dining_session_movements_immutable
                    BEFORE UPDATE OR DELETE ON tenancy.dining_session_movements
                    FOR EACH ROW EXECUTE FUNCTION tenancy.prevent_dining_session_movements_mutation();
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP TRIGGER IF EXISTS trg_dining_session_movements_immutable ON tenancy.dining_session_movements;
                DROP FUNCTION IF EXISTS tenancy.prevent_dining_session_movements_mutation();
            ");

            migrationBuilder.DropTable(
                name: "dining_session_movements",
                schema: "tenancy");
        }
    }
}
