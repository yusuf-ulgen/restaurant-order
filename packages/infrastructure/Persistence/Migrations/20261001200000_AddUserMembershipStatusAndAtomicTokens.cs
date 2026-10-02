using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantOrder.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserMembershipStatusAndAtomicTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Add status column to iam.memberships with Active (1) default
            migrationBuilder.AddColumn<int>(
                name: "status",
                schema: "iam",
                table: "memberships",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            // 2. Synchronize existing memberships status based on is_active
            migrationBuilder.Sql(@"
                UPDATE iam.memberships
                SET status = CASE WHEN is_active = true THEN 1 ELSE 3 END
                WHERE status = 0 OR status IS NULL;
            ");

            // 3. Pre-authentication lookup of all active memberships for a user
            migrationBuilder.Sql(@"
                CREATE OR REPLACE FUNCTION iam.lookup_active_memberships_by_user(p_user_id uuid)
                RETURNS TABLE (
                    membership_id uuid,
                    tenant_id uuid,
                    branch_id uuid,
                    role text,
                    status integer
                )
                LANGUAGE plpgsql
                SECURITY DEFINER
                SET search_path = iam, pg_temp
                AS $$
                BEGIN
                    RETURN QUERY
                    SELECT m.id, m.tenant_id, m.branch_id, m.role::text, m.status
                    FROM iam.memberships m
                    WHERE m.user_id = p_user_id 
                      AND m.status = 1 
                      AND m.is_active = true;
                END;
                $$;

                REVOKE ALL ON FUNCTION iam.lookup_active_memberships_by_user(uuid) FROM PUBLIC;
                GRANT EXECUTE ON FUNCTION iam.lookup_active_memberships_by_user(uuid) TO restaurant_app_runtime;
            ");

            // 4. Atomic single-statement invitation token consumption
            migrationBuilder.Sql(@"
                CREATE OR REPLACE FUNCTION iam.consume_invitation_token(p_token_hash text, p_now_utc timestamp with time zone)
                RETURNS TABLE (
                    invitation_id uuid,
                    tenant_id uuid,
                    user_id uuid
                )
                LANGUAGE plpgsql
                SECURITY DEFINER
                SET search_path = iam, pg_temp
                AS $$
                BEGIN
                    RETURN QUERY
                    UPDATE iam.invitation_tokens t
                    SET is_consumed = TRUE,
                        consumed_at_utc = p_now_utc
                    WHERE t.token_hash = p_token_hash
                      AND t.is_consumed = FALSE
                      AND t.expires_at_utc > p_now_utc
                    RETURNING t.id, t.tenant_id, t.user_id;
                END;
                $$;

                REVOKE ALL ON FUNCTION iam.consume_invitation_token(text, timestamp with time zone) FROM PUBLIC;
                GRANT EXECUTE ON FUNCTION iam.consume_invitation_token(text, timestamp with time zone) TO restaurant_app_runtime;
            ");

            // 5. Atomic single-statement password reset token consumption
            migrationBuilder.Sql(@"
                CREATE OR REPLACE FUNCTION iam.consume_password_reset_token(p_token_hash text, p_now_utc timestamp with time zone)
                RETURNS TABLE (
                    reset_token_id uuid,
                    tenant_id uuid,
                    user_id uuid
                )
                LANGUAGE plpgsql
                SECURITY DEFINER
                SET search_path = iam, pg_temp
                AS $$
                BEGIN
                    RETURN QUERY
                    UPDATE iam.password_reset_tokens t
                    SET is_consumed = TRUE,
                        consumed_at_utc = p_now_utc
                    WHERE t.token_hash = p_token_hash
                      AND t.is_consumed = FALSE
                      AND t.expires_at_utc > p_now_utc
                    RETURNING t.id, t.tenant_id, t.user_id;
                END;
                $$;

                REVOKE ALL ON FUNCTION iam.consume_password_reset_token(text, timestamp with time zone) FROM PUBLIC;
                GRANT EXECUTE ON FUNCTION iam.consume_password_reset_token(text, timestamp with time zone) TO restaurant_app_runtime;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS iam.consume_password_reset_token(text, timestamp with time zone);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS iam.consume_invitation_token(text, timestamp with time zone);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS iam.lookup_active_memberships_by_user(uuid);");

            migrationBuilder.DropColumn(
                name: "status",
                schema: "iam",
                table: "memberships");
        }
    }
}
