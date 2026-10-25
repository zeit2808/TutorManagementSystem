using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TutorManagement.Modules.IdentityAccess.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ConstrainPasswordResetOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddForeignKey(
                name: "FK_email_outbox_messages_user_security_tokens_security_token_id",
                schema: "iam",
                table: "email_outbox_messages",
                column: "security_token_id",
                principalSchema: "iam",
                principalTable: "user_security_tokens",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_email_outbox_messages_user_security_tokens_security_token_id",
                schema: "iam",
                table: "email_outbox_messages");
        }
    }
}
