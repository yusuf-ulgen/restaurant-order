using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantOrder.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOperationalOutboxClaimGateway : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. PostgreSQL secure claim gateway function with FOR UPDATE SKIP LOCKED
            // Enforces least-privilege, SECURITY DEFINER execution with fixed search_path.
            // Preserves FORCE ROW LEVEL SECURITY for application queries while providing a narrow,
            // lease-protected claim gateway for background worker dispatching across tenant queues.
            migrationBuilder.Sql(@"
                CREATE OR REPLACE FUNCTION tenancy.claim_pending_operational_outbox(
                    p_limit integer,
                    p_now_utc timestamp with time zone,
                    p_lease_seconds integer,
                    p_claim_id text,
                    p_tenant_id uuid DEFAULT NULL
                )
                RETURNS TABLE (
                    id uuid,
                    tenant_id uuid,
                    branch_id uuid,
                    aggregate_id text,
                    event_type text,
                    payload text,
                    schema_version integer,
                    sequence_number bigint,
                    occurred_at timestamp with time zone,
                    attempt_count integer,
                    max_attempts integer,
                    lock_claim_id text
                )
                LANGUAGE plpgsql
                SECURITY DEFINER
                SET search_path = tenancy, pg_temp
                AS $$
                BEGIN
                    RETURN QUERY
                    WITH cte AS (
                        SELECT o.id
                        FROM tenancy.operational_outbox o
                        WHERE ((o.status = 1 AND (o.next_attempt_utc IS NULL OR o.next_attempt_utc <= p_now_utc))
                            OR (o.status = 2 AND o.lock_expires_utc < p_now_utc))
                          AND (p_tenant_id IS NULL OR o.tenant_id = p_tenant_id)
                        ORDER BY o.created_at ASC
                        LIMIT p_limit
                        FOR UPDATE SKIP LOCKED
                    )
                    UPDATE tenancy.operational_outbox o
                    SET status = 2,
                        lock_claim_id = p_claim_id,
                        lock_expires_utc = p_now_utc + (p_lease_seconds || ' seconds')::interval,
                        attempt_count = o.attempt_count + 1
                    FROM cte
                    WHERE o.id = cte.id
                    RETURNING o.id, o.tenant_id, o.branch_id, o.aggregate_id::text, o.event_type::text, o.payload::text, o.schema_version, o.sequence_number, o.occurred_at, o.attempt_count, o.max_attempts, o.lock_claim_id::text;
                END;
                $$;

                REVOKE ALL ON FUNCTION tenancy.claim_pending_operational_outbox(integer, timestamp with time zone, integer, text, uuid) FROM PUBLIC;

                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'restaurant_app_runtime') THEN
                        GRANT EXECUTE ON FUNCTION tenancy.claim_pending_operational_outbox(integer, timestamp with time zone, integer, text, uuid) TO restaurant_app_runtime;
                    END IF;
                END $$;
            ");

            // 2. PostgreSQL completion function verifying claim ownership
            migrationBuilder.Sql(@"
                CREATE OR REPLACE FUNCTION tenancy.complete_operational_outbox(
                    p_id uuid,
                    p_claim_id text,
                    p_now_utc timestamp with time zone
                )
                RETURNS boolean
                LANGUAGE plpgsql
                SECURITY DEFINER
                SET search_path = tenancy, pg_temp
                AS $$
                DECLARE
                    v_rows integer;
                BEGIN
                    UPDATE tenancy.operational_outbox
                    SET status = 3,
                        dispatched_at = p_now_utc,
                        lock_claim_id = NULL,
                        lock_expires_utc = NULL
                    WHERE id = p_id
                      AND lock_claim_id = p_claim_id
                      AND status = 2;

                    GET DIAGNOSTICS v_rows = ROW_COUNT;
                    RETURN v_rows > 0;
                END;
                $$;

                REVOKE ALL ON FUNCTION tenancy.complete_operational_outbox(uuid, text, timestamp with time zone) FROM PUBLIC;

                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'restaurant_app_runtime') THEN
                        GRANT EXECUTE ON FUNCTION tenancy.complete_operational_outbox(uuid, text, timestamp with time zone) TO restaurant_app_runtime;
                    END IF;
                END $$;
            ");

            // 3. PostgreSQL failure & dead-letter function verifying claim ownership
            migrationBuilder.Sql(@"
                CREATE OR REPLACE FUNCTION tenancy.fail_operational_outbox(
                    p_id uuid,
                    p_claim_id text,
                    p_error text,
                    p_next_attempt_utc timestamp with time zone,
                    p_is_dead_letter boolean,
                    p_now_utc timestamp with time zone
                )
                RETURNS boolean
                LANGUAGE plpgsql
                SECURITY DEFINER
                SET search_path = tenancy, pg_temp
                AS $$
                DECLARE
                    v_rows integer;
                BEGIN
                    UPDATE tenancy.operational_outbox
                    SET status = CASE WHEN p_is_dead_letter THEN 4 ELSE 1 END,
                        lock_claim_id = NULL,
                        lock_expires_utc = NULL,
                        last_error = substring(p_error from 1 for 500),
                        next_attempt_utc = p_next_attempt_utc
                    WHERE id = p_id
                      AND lock_claim_id = p_claim_id
                      AND status = 2;

                    GET DIAGNOSTICS v_rows = ROW_COUNT;
                    RETURN v_rows > 0;
                END;
                $$;

                REVOKE ALL ON FUNCTION tenancy.fail_operational_outbox(uuid, text, text, timestamp with time zone, boolean, timestamp with time zone) FROM PUBLIC;

                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'restaurant_app_runtime') THEN
                        GRANT EXECUTE ON FUNCTION tenancy.fail_operational_outbox(uuid, text, text, timestamp with time zone, boolean, timestamp with time zone) TO restaurant_app_runtime;
                    END IF;
                END $$;
            ");

            // 4. PostgreSQL claim renewal function for long batches
            migrationBuilder.Sql(@"
                CREATE OR REPLACE FUNCTION tenancy.renew_operational_outbox_claim(
                    p_id uuid,
                    p_claim_id text,
                    p_lease_seconds integer,
                    p_now_utc timestamp with time zone
                )
                RETURNS boolean
                LANGUAGE plpgsql
                SECURITY DEFINER
                SET search_path = tenancy, pg_temp
                AS $$
                DECLARE
                    v_rows integer;
                BEGIN
                    UPDATE tenancy.operational_outbox
                    SET lock_expires_utc = p_now_utc + (p_lease_seconds || ' seconds')::interval
                    WHERE id = p_id
                      AND lock_claim_id = p_claim_id
                      AND status = 2;

                    GET DIAGNOSTICS v_rows = ROW_COUNT;
                    RETURN v_rows > 0;
                END;
                $$;

                REVOKE ALL ON FUNCTION tenancy.renew_operational_outbox_claim(uuid, text, integer, timestamp with time zone) FROM PUBLIC;

                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'restaurant_app_runtime') THEN
                        GRANT EXECUTE ON FUNCTION tenancy.renew_operational_outbox_claim(uuid, text, integer, timestamp with time zone) TO restaurant_app_runtime;
                    END IF;
                END $$;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS tenancy.renew_operational_outbox_claim(uuid, text, integer, timestamp with time zone);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS tenancy.fail_operational_outbox(uuid, text, text, timestamp with time zone, boolean, timestamp with time zone);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS tenancy.complete_operational_outbox(uuid, text, timestamp with time zone);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS tenancy.claim_pending_operational_outbox(integer, timestamp with time zone, integer, text, uuid);");
        }
    }
}
