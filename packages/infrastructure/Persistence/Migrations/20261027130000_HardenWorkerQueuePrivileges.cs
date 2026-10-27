using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace RestaurantOrder.Infrastructure.Persistence.Migrations;

[DbContext(typeof(RestaurantOrderDbContext))]
[Migration("20261027130000_HardenWorkerQueuePrivileges")]
public sealed class HardenWorkerQueuePrivileges : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$ BEGIN
                IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'restaurant_worker_runtime') THEN
                    CREATE ROLE restaurant_worker_runtime NOLOGIN NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE;
                END IF;
                IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'restaurant_queue_gateway') THEN
                    CREATE ROLE restaurant_queue_gateway NOLOGIN NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE;
                END IF;
            END $$;
            GRANT USAGE ON SCHEMA tenancy, iam TO restaurant_worker_runtime;
            GRANT EXECUTE ON FUNCTION tenancy.get_current_tenant_id() TO restaurant_worker_runtime;
            GRANT SELECT, UPDATE ON tenancy.operational_outbox TO restaurant_worker_runtime;
            GRANT SELECT, INSERT, UPDATE ON tenancy.notification_deliveries TO restaurant_worker_runtime;
            GRANT SELECT, UPDATE ON tenancy.device_registrations TO restaurant_worker_runtime;
            GRANT SELECT ON tenancy.dining_sessions, iam.memberships TO restaurant_worker_runtime;
            GRANT SELECT (id, status) ON iam.users TO restaurant_worker_runtime;
            GRANT USAGE ON SCHEMA tenancy TO restaurant_queue_gateway;
            GRANT SELECT, UPDATE ON tenancy.operational_outbox, tenancy.notification_deliveries TO restaurant_queue_gateway;
            CREATE POLICY queue_gateway_outbox ON tenancy.operational_outbox
                TO restaurant_queue_gateway USING (true) WITH CHECK (true);
            CREATE POLICY queue_gateway_push ON tenancy.notification_deliveries
                TO restaurant_queue_gateway USING (true) WITH CHECK (true);

            DO $roles$
            DECLARE f record; definition text;
            BEGIN
                FOR f IN SELECT p.oid, p.proname, p.oid::regprocedure AS signature
                    FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace
                    WHERE n.nspname = 'tenancy' AND p.proname IN
                        ('claim_pending_operational_outbox', 'claim_pending_notification_deliveries',
                         'complete_operational_outbox', 'fail_operational_outbox', 'renew_operational_outbox_claim')
                LOOP
                    definition := pg_get_functiondef(f.oid);
                    IF f.proname IN ('complete_operational_outbox', 'fail_operational_outbox', 'renew_operational_outbox_claim') THEN
                        definition := replace(definition, 'AND status = 2;',
                            'AND status = 2 AND lock_expires_utc > clock_timestamp();');
                    END IF;
                    EXECUTE definition;
                    EXECUTE format('ALTER FUNCTION %s SET search_path = pg_catalog, tenancy, pg_temp', f.signature);
                    EXECUTE format('REVOKE ALL ON FUNCTION %s FROM PUBLIC, restaurant_app_runtime', f.signature);
                    EXECUTE format('GRANT EXECUTE ON FUNCTION %s TO restaurant_worker_runtime', f.signature);
                    EXECUTE format('ALTER FUNCTION %s OWNER TO restaurant_queue_gateway', f.signature);
                END LOOP;
            END $roles$;

            CREATE UNIQUE INDEX ux_notification_deliveries_event_device
                ON tenancy.notification_deliveries (tenant_id, event_id, device_registration_id);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Geri dönüş API rolüne global kuyruk yetkisini tekrar vermez.
        migrationBuilder.Sql("DROP INDEX IF EXISTS tenancy.ux_notification_deliveries_event_device;");
    }
}
