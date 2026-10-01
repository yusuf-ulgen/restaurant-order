using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantOrder.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIamPersistenceAndTenantIsolation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "iam");

            migrationBuilder.AddUniqueConstraint(
                name: "ak_branches_tenant_id_id",
                schema: "tenancy",
                table: "branches",
                columns: new[] { "tenant_id", "id" });

            migrationBuilder.CreateTable(
                name: "security_audit_events",
                schema: "iam",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    ip_address = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    user_agent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    details_json = table.Column<string>(type: "text", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_security_audit_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_security_audit_events_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "tenancy",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "trusted_terminals",
                schema: "iam",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_identifier = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    terminal_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    secret_hash = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    last_heartbeat_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    enrolled_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trusted_terminals", x => x.id);
                    table.ForeignKey(
                        name: "fk_trusted_terminals_branches_tenant_id_branch_id",
                        columns: x => new { x.tenant_id, x.branch_id },
                        principalSchema: "tenancy",
                        principalTable: "branches",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_trusted_terminals_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "tenancy",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "users",
                schema: "iam",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    normalized_email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    security_version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    failed_login_attempts = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    lockout_end_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    concurrency_token = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "invitation_tokens",
                schema: "iam",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    is_consumed = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    consumed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_invitation_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_invitation_tokens_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "tenancy",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_invitation_tokens_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "iam",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "memberships",
                schema: "iam",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<int>(type: "integer", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    concurrency_token = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_memberships", x => x.id);
                    table.ForeignKey(
                        name: "fk_memberships_branches_tenant_id_branch_id",
                        columns: x => new { x.tenant_id, x.branch_id },
                        principalSchema: "tenancy",
                        principalTable: "branches",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_memberships_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "tenancy",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_memberships_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "iam",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "password_reset_tokens",
                schema: "iam",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    is_consumed = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    consumed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_password_reset_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_password_reset_tokens_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "tenancy",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_password_reset_tokens_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "iam",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pin_credentials",
                schema: "iam",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pin_hash = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    algorithm_version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    pepper_key_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    failed_pin_attempts = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    locked_until_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pin_credentials", x => x.id);
                    table.ForeignKey(
                        name: "fk_pin_credentials_branches_tenant_id_branch_id",
                        columns: x => new { x.tenant_id, x.branch_id },
                        principalSchema: "tenancy",
                        principalTable: "branches",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pin_credentials_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "tenancy",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pin_credentials_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "iam",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sessions",
                schema: "iam",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    membership_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    auth_method = table.Column<int>(type: "integer", nullable: false),
                    ip_address = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    user_agent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_revoked = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    revoked_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revocation_reason = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sessions", x => x.id);
                    table.ForeignKey(
                        name: "fk_sessions_memberships_membership_id",
                        column: x => x.membership_id,
                        principalSchema: "iam",
                        principalTable: "memberships",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_sessions_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "tenancy",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sessions_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "iam",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                schema: "iam",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_family_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    is_revoked = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    revoked_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    replaced_by_token_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_refresh_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_refresh_tokens_sessions_session_id",
                        column: x => x.session_id,
                        principalSchema: "iam",
                        principalTable: "sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_refresh_tokens_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "tenancy",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_invitation_tokens_user_id",
                schema: "iam",
                table: "invitation_tokens",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_invitation_tokens_tenant_id",
                schema: "iam",
                table: "invitation_tokens",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_invitation_tokens_token_hash",
                schema: "iam",
                table: "invitation_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_memberships_tenant_branch",
                schema: "iam",
                table: "memberships",
                columns: new[] { "tenant_id", "branch_id" });

            migrationBuilder.CreateIndex(
                name: "ix_memberships_tenant_id",
                schema: "iam",
                table: "memberships",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_memberships_user_id",
                schema: "iam",
                table: "memberships",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_password_reset_tokens_user_id",
                schema: "iam",
                table: "password_reset_tokens",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_password_reset_tokens_tenant_id",
                schema: "iam",
                table: "password_reset_tokens",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_password_reset_tokens_token_hash",
                schema: "iam",
                table: "password_reset_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pin_credentials_tenant_id_branch_id",
                schema: "iam",
                table: "pin_credentials",
                columns: new[] { "tenant_id", "branch_id" });

            migrationBuilder.CreateIndex(
                name: "IX_pin_credentials_user_id",
                schema: "iam",
                table: "pin_credentials",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_pin_credentials_tenant_user_branch",
                schema: "iam",
                table: "pin_credentials",
                columns: new[] { "tenant_id", "user_id", "branch_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_session_id",
                schema: "iam",
                table: "refresh_tokens",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_tenant_id",
                schema: "iam",
                table: "refresh_tokens",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_token_family_id",
                schema: "iam",
                table: "refresh_tokens",
                column: "token_family_id");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_token_hash",
                schema: "iam",
                table: "refresh_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_security_audit_events_created_at_utc",
                schema: "iam",
                table: "security_audit_events",
                column: "created_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_security_audit_events_event_type",
                schema: "iam",
                table: "security_audit_events",
                column: "event_type");

            migrationBuilder.CreateIndex(
                name: "ix_security_audit_events_tenant_id",
                schema: "iam",
                table: "security_audit_events",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_security_audit_events_user_id",
                schema: "iam",
                table: "security_audit_events",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_sessions_expires_at_utc",
                schema: "iam",
                table: "sessions",
                column: "expires_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_sessions_membership_id",
                schema: "iam",
                table: "sessions",
                column: "membership_id");

            migrationBuilder.CreateIndex(
                name: "ix_sessions_tenant_id",
                schema: "iam",
                table: "sessions",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_sessions_user_id",
                schema: "iam",
                table: "sessions",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_trusted_terminals_tenant_branch_device",
                schema: "iam",
                table: "trusted_terminals",
                columns: new[] { "tenant_id", "branch_id", "device_identifier" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_normalized_email",
                schema: "iam",
                table: "users",
                column: "normalized_email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "invitation_tokens",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "password_reset_tokens",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "pin_credentials",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "refresh_tokens",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "security_audit_events",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "trusted_terminals",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "sessions",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "memberships",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "users",
                schema: "iam");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_branches_tenant_id_id",
                schema: "tenancy",
                table: "branches");
        }
    }
}
