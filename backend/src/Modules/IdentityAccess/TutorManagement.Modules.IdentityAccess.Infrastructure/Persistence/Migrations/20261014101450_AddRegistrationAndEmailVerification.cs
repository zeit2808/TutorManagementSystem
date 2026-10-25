using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TutorManagement.Modules.IdentityAccess.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRegistrationAndEmailVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameIndex(
                name: "IX_users_normalized_username",
                schema: "iam",
                table: "users",
                newName: "ux_users_normalized_username");

            migrationBuilder.AddColumn<string>(
                name: "email",
                schema: "iam",
                table: "users",
                type: "character varying(254)",
                maxLength: 254,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "email_verified_at_utc",
                schema: "iam",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "normalized_email",
                schema: "iam",
                table: "users",
                type: "character varying(254)",
                maxLength: 254,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "phone_number",
                schema: "iam",
                table: "users",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.Sql(
                """
                WITH numbered_users AS (
                    SELECT id, ROW_NUMBER() OVER (ORDER BY created_at_utc, id) AS row_number
                    FROM iam.users
                )
                UPDATE iam.users AS users
                SET email = LOWER(users.username) || '.' || REPLACE(users.id::text, '-', '') || '@local.invalid',
                    normalized_email = UPPER(users.username) || '.' || UPPER(REPLACE(users.id::text, '-', '')) || '@LOCAL.INVALID',
                    phone_number = '+849' || LPAD(numbered_users.row_number::text, 8, '0'),
                    email_verified_at_utc = users.created_at_utc
                FROM numbered_users
                WHERE users.id = numbered_users.id;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "email",
                schema: "iam",
                table: "users",
                type: "character varying(254)",
                maxLength: 254,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(254)",
                oldMaxLength: 254,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "normalized_email",
                schema: "iam",
                table: "users",
                type: "character varying(254)",
                maxLength: 254,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(254)",
                oldMaxLength: 254,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "phone_number",
                schema: "iam",
                table: "users",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "email_outbox_messages",
                schema: "iam",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_email_outbox_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "user_security_tokens",
                schema: "iam",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purpose = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    token_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    used_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_security_tokens", x => x.id);
                    table.ForeignKey(
                        name: "FK_user_security_tokens_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "iam",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_users_normalized_email",
                schema: "iam",
                table: "users",
                column: "normalized_email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_users_phone_number",
                schema: "iam",
                table: "users",
                column: "phone_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_email_outbox_messages_processed_at_utc_created_at_utc",
                schema: "iam",
                table: "email_outbox_messages",
                columns: new[] { "processed_at_utc", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_user_security_tokens_user_id_purpose_expires_at_utc",
                schema: "iam",
                table: "user_security_tokens",
                columns: new[] { "user_id", "purpose", "expires_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ux_user_security_tokens_token_hash",
                schema: "iam",
                table: "user_security_tokens",
                column: "token_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "email_outbox_messages",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "user_security_tokens",
                schema: "iam");

            migrationBuilder.DropIndex(
                name: "ux_users_normalized_email",
                schema: "iam",
                table: "users");

            migrationBuilder.DropIndex(
                name: "ux_users_phone_number",
                schema: "iam",
                table: "users");

            migrationBuilder.DropColumn(
                name: "email",
                schema: "iam",
                table: "users");

            migrationBuilder.DropColumn(
                name: "email_verified_at_utc",
                schema: "iam",
                table: "users");

            migrationBuilder.DropColumn(
                name: "normalized_email",
                schema: "iam",
                table: "users");

            migrationBuilder.DropColumn(
                name: "phone_number",
                schema: "iam",
                table: "users");

            migrationBuilder.RenameIndex(
                name: "ux_users_normalized_username",
                schema: "iam",
                table: "users",
                newName: "IX_users_normalized_username");
        }
    }
}
