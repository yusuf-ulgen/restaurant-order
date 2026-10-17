using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantOrder.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrdersAndOrderPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "orders",
                schema: "tenancy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dining_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    table_id = table.Column<Guid>(type: "uuid", nullable: false),
                    round_number = table.Column<int>(type: "integer", nullable: false),
                    order_source = table.Column<int>(type: "integer", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    subtotal_minor_units = table.Column<long>(type: "bigint", nullable: false),
                    tax_minor_units = table.Column<long>(type: "bigint", nullable: false),
                    service_charge_minor_units = table.Column<long>(type: "bigint", nullable: false),
                    total_minor_units = table.Column<long>(type: "bigint", nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    request_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    concurrency_token = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    submitted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancellation_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    cancelled_by_actor_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_orders", x => x.id);
                    table.CheckConstraint("ck_orders_round_number", "round_number >= 1");
                    table.CheckConstraint("ck_orders_service_charge", "service_charge_minor_units >= 0");
                    table.CheckConstraint("ck_orders_source", "order_source IN (1, 2)");
                    table.CheckConstraint("ck_orders_status", "status IN (1, 2, 3, 4, 5, 6)");
                    table.CheckConstraint("ck_orders_subtotal", "subtotal_minor_units >= 0");
                    table.CheckConstraint("ck_orders_tax", "tax_minor_units >= 0");
                    table.CheckConstraint("ck_orders_total", "total_minor_units >= 0");
                    table.ForeignKey(
                        name: "fk_orders_dining_session",
                        columns: x => new { x.tenant_id, x.branch_id, x.dining_session_id },
                        principalSchema: "tenancy",
                        principalTable: "dining_sessions",
                        principalColumns: new[] { "tenant_id", "branch_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_orders_table",
                        columns: x => new { x.tenant_id, x.branch_id, x.table_id },
                        principalSchema: "tenancy",
                        principalTable: "restaurant_tables",
                        principalColumns: new[] { "tenant_id", "branch_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "order_audit_entries",
                schema: "tenancy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dining_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_role = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    action = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    previous_status = table.Column<int>(type: "integer", nullable: true),
                    new_status = table.Column<int>(type: "integer", nullable: true),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    details_json = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_audit_entries", x => x.id);
                    table.CheckConstraint("ck_order_audit_entries_action", "length(action) >= 1");
                    table.ForeignKey(
                        name: "fk_order_audit_entries_order",
                        column: x => x.order_id,
                        principalSchema: "tenancy",
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "order_items",
                schema: "tenancy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    menu_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_variant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    item_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    variant_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    customer_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    preparation_station_id = table.Column<Guid>(type: "uuid", nullable: true),
                    unit_price_minor_units = table.Column<long>(type: "bigint", nullable: false),
                    modifier_total_minor_units = table.Column<long>(type: "bigint", nullable: false),
                    line_total_minor_units = table.Column<long>(type: "bigint", nullable: false),
                    tax_rate_basis_points = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_items", x => x.id);
                    table.CheckConstraint("ck_order_items_line_total", "line_total_minor_units >= 0");
                    table.CheckConstraint("ck_order_items_modifier_total", "modifier_total_minor_units >= 0");
                    table.CheckConstraint("ck_order_items_quantity", "quantity >= 1 AND quantity <= 99");
                    table.CheckConstraint("ck_order_items_status", "status IN (1, 2, 3, 4, 5, 6)");
                    table.CheckConstraint("ck_order_items_tax_rate", "tax_rate_basis_points >= 0 AND tax_rate_basis_points <= 10000");
                    table.CheckConstraint("ck_order_items_unit_price", "unit_price_minor_units >= 0");
                    table.ForeignKey(
                        name: "fk_order_items_item_variant",
                        column: x => x.item_variant_id,
                        principalSchema: "tenancy",
                        principalTable: "item_variants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_order_items_menu_item",
                        column: x => x.menu_item_id,
                        principalSchema: "tenancy",
                        principalTable: "menu_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_order_items_order",
                        column: x => x.order_id,
                        principalSchema: "tenancy",
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "order_item_modifiers",
                schema: "tenancy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    modifier_group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    modifier_group_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    modifier_option_id = table.Column<Guid>(type: "uuid", nullable: false),
                    modifier_option_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    price_delta_minor_units = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_item_modifiers", x => x.id);
                    table.CheckConstraint("ck_order_item_modifiers_price_delta", "price_delta_minor_units >= 0");
                    table.ForeignKey(
                        name: "fk_order_item_modifiers_item",
                        column: x => x.order_item_id,
                        principalSchema: "tenancy",
                        principalTable: "order_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_order_audit_entries_order_id",
                schema: "tenancy",
                table: "order_audit_entries",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_order_audit_entries_tenant_created",
                schema: "tenancy",
                table: "order_audit_entries",
                columns: new[] { "tenant_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_order_audit_entries_tenant_order",
                schema: "tenancy",
                table: "order_audit_entries",
                columns: new[] { "tenant_id", "order_id" });

            migrationBuilder.CreateIndex(
                name: "ix_order_audit_entries_tenant_session",
                schema: "tenancy",
                table: "order_audit_entries",
                columns: new[] { "tenant_id", "dining_session_id" });

            migrationBuilder.CreateIndex(
                name: "ix_order_item_modifiers_item_id",
                schema: "tenancy",
                table: "order_item_modifiers",
                column: "order_item_id");

            migrationBuilder.CreateIndex(
                name: "IX_order_items_item_variant_id",
                schema: "tenancy",
                table: "order_items",
                column: "item_variant_id");

            migrationBuilder.CreateIndex(
                name: "IX_order_items_menu_item_id",
                schema: "tenancy",
                table: "order_items",
                column: "menu_item_id");

            migrationBuilder.CreateIndex(
                name: "IX_order_items_order_id",
                schema: "tenancy",
                table: "order_items",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_order_items_tenant_order",
                schema: "tenancy",
                table: "order_items",
                columns: new[] { "tenant_id", "order_id" });

            migrationBuilder.CreateIndex(
                name: "ix_order_items_tenant_station",
                schema: "tenancy",
                table: "order_items",
                columns: new[] { "tenant_id", "preparation_station_id" });

            migrationBuilder.CreateIndex(
                name: "ix_order_items_tenant_status",
                schema: "tenancy",
                table: "order_items",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_orders_tenant_id_branch_id_dining_session_id",
                schema: "tenancy",
                table: "orders",
                columns: new[] { "tenant_id", "branch_id", "dining_session_id" });

            migrationBuilder.CreateIndex(
                name: "IX_orders_tenant_id_branch_id_table_id",
                schema: "tenancy",
                table: "orders",
                columns: new[] { "tenant_id", "branch_id", "table_id" });

            migrationBuilder.CreateIndex(
                name: "ix_orders_tenant_branch_status",
                schema: "tenancy",
                table: "orders",
                columns: new[] { "tenant_id", "branch_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_orders_tenant_created",
                schema: "tenancy",
                table: "orders",
                columns: new[] { "tenant_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_orders_tenant_dining_session",
                schema: "tenancy",
                table: "orders",
                columns: new[] { "tenant_id", "dining_session_id" });

            migrationBuilder.CreateIndex(
                name: "ix_orders_tenant_idempotency_key",
                schema: "tenancy",
                table: "orders",
                columns: new[] { "tenant_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_orders_tenant_session_round",
                schema: "tenancy",
                table: "orders",
                columns: new[] { "tenant_id", "dining_session_id", "round_number" },
                unique: true);

            // PostgreSQL Row Level Security (RLS) enforcement
            migrationBuilder.Sql(@"
                ALTER TABLE tenancy.orders ENABLE ROW LEVEL SECURITY;
                ALTER TABLE tenancy.orders FORCE ROW LEVEL SECURITY;

                DROP POLICY IF EXISTS orders_isolation_policy ON tenancy.orders;
                CREATE POLICY orders_isolation_policy ON tenancy.orders
                    FOR ALL
                    USING (tenant_id = tenancy.get_current_tenant_id())
                    WITH CHECK (tenant_id = tenancy.get_current_tenant_id());

                ALTER TABLE tenancy.order_items ENABLE ROW LEVEL SECURITY;
                ALTER TABLE tenancy.order_items FORCE ROW LEVEL SECURITY;

                DROP POLICY IF EXISTS order_items_isolation_policy ON tenancy.order_items;
                CREATE POLICY order_items_isolation_policy ON tenancy.order_items
                    FOR ALL
                    USING (tenant_id = tenancy.get_current_tenant_id())
                    WITH CHECK (tenant_id = tenancy.get_current_tenant_id());

                ALTER TABLE tenancy.order_item_modifiers ENABLE ROW LEVEL SECURITY;
                ALTER TABLE tenancy.order_item_modifiers FORCE ROW LEVEL SECURITY;

                DROP POLICY IF EXISTS order_item_modifiers_isolation_policy ON tenancy.order_item_modifiers;
                CREATE POLICY order_item_modifiers_isolation_policy ON tenancy.order_item_modifiers
                    FOR ALL
                    USING (EXISTS (SELECT 1 FROM tenancy.order_items oi WHERE oi.id = order_item_modifiers.order_item_id AND oi.tenant_id = tenancy.get_current_tenant_id()))
                    WITH CHECK (EXISTS (SELECT 1 FROM tenancy.order_items oi WHERE oi.id = order_item_modifiers.order_item_id AND oi.tenant_id = tenancy.get_current_tenant_id()));

                ALTER TABLE tenancy.order_audit_entries ENABLE ROW LEVEL SECURITY;
                ALTER TABLE tenancy.order_audit_entries FORCE ROW LEVEL SECURITY;

                DROP POLICY IF EXISTS order_audit_entries_isolation_policy ON tenancy.order_audit_entries;
                CREATE POLICY order_audit_entries_isolation_policy ON tenancy.order_audit_entries
                    FOR ALL
                    USING (tenant_id = tenancy.get_current_tenant_id())
                    WITH CHECK (tenant_id = tenancy.get_current_tenant_id());

                -- Immutable audit trigger: strictly prevents updates and deletes on order_audit_entries
                CREATE OR REPLACE FUNCTION tenancy.prevent_order_audit_update_delete()
                RETURNS TRIGGER AS $$
                BEGIN
                    RAISE EXCEPTION 'Order audit entries are strictly immutable and cannot be updated or deleted.';
                END;
                $$ LANGUAGE plpgsql;

                DROP TRIGGER IF EXISTS trg_prevent_order_audit_update_delete ON tenancy.order_audit_entries;
                CREATE TRIGGER trg_prevent_order_audit_update_delete
                    BEFORE UPDATE OR DELETE ON tenancy.order_audit_entries
                    FOR EACH ROW
                    EXECUTE FUNCTION tenancy.prevent_order_audit_update_delete();

                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'restaurant_app_runtime') THEN
                        GRANT SELECT, INSERT, UPDATE, DELETE ON tenancy.orders TO restaurant_app_runtime;
                        GRANT SELECT, INSERT, UPDATE, DELETE ON tenancy.order_items TO restaurant_app_runtime;
                        GRANT SELECT, INSERT, UPDATE, DELETE ON tenancy.order_item_modifiers TO restaurant_app_runtime;
                        GRANT SELECT, INSERT ON tenancy.order_audit_entries TO restaurant_app_runtime;
                    END IF;
                END $$;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP TRIGGER IF EXISTS trg_prevent_order_audit_update_delete ON tenancy.order_audit_entries;
                DROP FUNCTION IF EXISTS tenancy.prevent_order_audit_update_delete();
                DROP POLICY IF EXISTS order_audit_entries_isolation_policy ON tenancy.order_audit_entries;
                DROP POLICY IF EXISTS order_item_modifiers_isolation_policy ON tenancy.order_item_modifiers;
                DROP POLICY IF EXISTS order_items_isolation_policy ON tenancy.order_items;
                DROP POLICY IF EXISTS orders_isolation_policy ON tenancy.orders;
            ");

            migrationBuilder.DropTable(
                name: "order_audit_entries",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "order_item_modifiers",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "order_items",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "orders",
                schema: "tenancy");
        }
    }
}
