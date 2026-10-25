using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TutorManagement.Modules.IdentityAccess.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPasswordRecoverySessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "failed_attempt_count",
                schema: "iam",
                table: "user_security_tokens",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "version",
                schema: "iam",
                table: "user_security_tokens",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "security_token_id",
                schema: "iam",
                table: "email_outbox_messages",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "auth_sessions",
                schema: "iam",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revocation_reason = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_auth_sessions", x => x.id);
                    table.ForeignKey(
                        name: "FK_auth_sessions_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "iam",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_email_outbox_messages_security_token_id",
                schema: "iam",
                table: "email_outbox_messages",
                column: "security_token_id");

            migrationBuilder.CreateIndex(
                name: "IX_auth_sessions_user_id_revoked_at_utc",
                schema: "iam",
                table: "auth_sessions",
                columns: new[] { "user_id", "revoked_at_utc" });

            // Existing refresh-token families remain usable after deployment. Old
            // access JWTs lack sid and must be refreshed once to obtain a new JWT.
            migrationBuilder.Sql("""
                INSERT INTO iam.auth_sessions
                    (id, user_id, created_at_utc, revoked_at_utc, revocation_reason, version)
                SELECT family_id, user_id, MIN(created_at_utc),
                    CASE WHEN BOOL_OR(revoked_at_utc IS NULL) THEN NULL ELSE MAX(revoked_at_utc) END,
                    CASE WHEN BOOL_OR(revoked_at_utc IS NULL) THEN NULL ELSE 'migration_prior_revocation' END,
                    0
                FROM iam.refresh_tokens
                GROUP BY family_id, user_id
                ON CONFLICT (id) DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "auth_sessions",
                schema: "iam");

            migrationBuilder.DropIndex(
                name: "IX_email_outbox_messages_security_token_id",
                schema: "iam",
                table: "email_outbox_messages");

            migrationBuilder.DropColumn(
                name: "failed_attempt_count",
                schema: "iam",
                table: "user_security_tokens");

            migrationBuilder.DropColumn(
                name: "version",
                schema: "iam",
                table: "user_security_tokens");

            migrationBuilder.DropColumn(
                name: "security_token_id",
                schema: "iam",
                table: "email_outbox_messages");
        }
    }
}
