using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantOrder.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ClearClaimedAtUtcInOutboxCompletion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                CREATE OR REPLACE FUNCTION iam.complete_identity_notification(
                    p_id uuid,
                    p_claim_token uuid,
                    p_now_utc timestamp with time zone
                )
                RETURNS boolean
                LANGUAGE plpgsql
                SECURITY DEFINER
                SET search_path = iam, pg_temp
                AS $$
                DECLARE
                    v_rows integer;
                BEGIN
                    UPDATE iam.identity_notifications_outbox
                    SET status = 3,
                        claim_token = NULL,
                        claimed_at_utc = NULL,
                        locked_until_utc = NULL,
                        delivered_at_utc = p_now_utc,
                        updated_at_utc = p_now_utc
                    WHERE id = p_id
                      AND claim_token = p_claim_token
                      AND status = 2;

                    GET DIAGNOSTICS v_rows = ROW_COUNT;
                    RETURN v_rows > 0;
                END;
                $$;

                CREATE OR REPLACE FUNCTION iam.fail_identity_notification(
                    p_id uuid,
                    p_claim_token uuid,
                    p_error text,
                    p_next_attempt_utc timestamp with time zone,
                    p_is_dead_letter boolean,
                    p_now_utc timestamp with time zone
                )
                RETURNS boolean
                LANGUAGE plpgsql
                SECURITY DEFINER
                SET search_path = iam, pg_temp
                AS $$
                DECLARE
                    v_rows integer;
                BEGIN
                    UPDATE iam.identity_notifications_outbox
                    SET status = CASE WHEN p_is_dead_letter THEN 4 ELSE 1 END,
                        claim_token = NULL,
                        claimed_at_utc = NULL,
                        locked_until_utc = NULL,
                        last_error = substring(p_error from 1 for 500),
                        next_attempt_utc = p_next_attempt_utc,
                        updated_at_utc = p_now_utc
                    WHERE id = p_id
                      AND claim_token = p_claim_token
                      AND status = 2;

                    GET DIAGNOSTICS v_rows = ROW_COUNT;
                    RETURN v_rows > 0;
                END;
                $$;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                CREATE OR REPLACE FUNCTION iam.complete_identity_notification(
                    p_id uuid,
                    p_claim_token uuid,
                    p_now_utc timestamp with time zone
                )
                RETURNS boolean
                LANGUAGE plpgsql
                SECURITY DEFINER
                SET search_path = iam, pg_temp
                AS $$
                DECLARE
                    v_rows integer;
                BEGIN
                    UPDATE iam.identity_notifications_outbox
                    SET status = 3,
                        claim_token = NULL,
                        locked_until_utc = NULL,
                        delivered_at_utc = p_now_utc,
                        updated_at_utc = p_now_utc
                    WHERE id = p_id
                      AND claim_token = p_claim_token
                      AND status = 2;

                    GET DIAGNOSTICS v_rows = ROW_COUNT;
                    RETURN v_rows > 0;
                END;
                $$;

                CREATE OR REPLACE FUNCTION iam.fail_identity_notification(
                    p_id uuid,
                    p_claim_token uuid,
                    p_error text,
                    p_next_attempt_utc timestamp with time zone,
                    p_is_dead_letter boolean,
                    p_now_utc timestamp with time zone
                )
                RETURNS boolean
                LANGUAGE plpgsql
                SECURITY DEFINER
                SET search_path = iam, pg_temp
                AS $$
                DECLARE
                    v_rows integer;
                BEGIN
                    UPDATE iam.identity_notifications_outbox
                    SET status = CASE WHEN p_is_dead_letter THEN 4 ELSE 1 END,
                        claim_token = NULL,
                        locked_until_utc = NULL,
                        last_error = substring(p_error from 1 for 500),
                        next_attempt_utc = p_next_attempt_utc,
                        updated_at_utc = p_now_utc
                    WHERE id = p_id
                      AND claim_token = p_claim_token
                      AND status = 2;

                    GET DIAGNOSTICS v_rows = ROW_COUNT;
                    RETURN v_rows > 0;
                END;
                $$;
            ");
        }
    }
}
