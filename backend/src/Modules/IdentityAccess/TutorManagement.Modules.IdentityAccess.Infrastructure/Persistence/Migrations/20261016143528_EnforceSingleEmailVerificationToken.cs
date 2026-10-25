using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TutorManagement.Modules.IdentityAccess.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnforceSingleEmailVerificationToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "revocation_reason",
                schema: "iam",
                table: "user_security_tokens",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "revoked_at_utc",
                schema: "iam",
                table: "user_security_tokens",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql(
                """
                WITH ranked_tokens AS (
                    SELECT id,
                           ROW_NUMBER() OVER (
                               PARTITION BY user_id, purpose
                               ORDER BY created_at_utc DESC, id DESC) AS token_rank
                    FROM iam.user_security_tokens
                    WHERE used_at_utc IS NULL
                )
                UPDATE iam.user_security_tokens AS tokens
                SET revoked_at_utc = CURRENT_TIMESTAMP,
                    revocation_reason = 'superseded_by_migration'
                FROM ranked_tokens
                WHERE tokens.id = ranked_tokens.id
                  AND ranked_tokens.token_rank > 1;
                """);

            migrationBuilder.CreateIndex(
                name: "ux_user_security_tokens_unconsumed",
                schema: "iam",
                table: "user_security_tokens",
                columns: new[] { "user_id", "purpose" },
                unique: true,
                filter: "used_at_utc IS NULL AND revoked_at_utc IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_user_security_tokens_unconsumed",
                schema: "iam",
                table: "user_security_tokens");

            migrationBuilder.DropColumn(
                name: "revocation_reason",
                schema: "iam",
                table: "user_security_tokens");

            migrationBuilder.DropColumn(
                name: "revoked_at_utc",
                schema: "iam",
                table: "user_security_tokens");
        }
    }
}
