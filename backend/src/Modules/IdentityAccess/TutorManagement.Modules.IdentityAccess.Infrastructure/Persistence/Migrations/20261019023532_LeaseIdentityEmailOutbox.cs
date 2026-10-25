using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TutorManagement.Modules.IdentityAccess.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LeaseIdentityEmailOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "processing_id",
                schema: "iam",
                table: "email_outbox_messages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "processing_until_utc",
                schema: "iam",
                table: "email_outbox_messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_email_outbox_messages_processed_at_utc_processing_until_utc~",
                schema: "iam",
                table: "email_outbox_messages",
                columns: new[] { "processed_at_utc", "processing_until_utc", "created_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_email_outbox_messages_processed_at_utc_processing_until_utc~",
                schema: "iam",
                table: "email_outbox_messages");

            migrationBuilder.DropColumn(
                name: "processing_id",
                schema: "iam",
                table: "email_outbox_messages");

            migrationBuilder.DropColumn(
                name: "processing_until_utc",
                schema: "iam",
                table: "email_outbox_messages");
        }
    }
}
