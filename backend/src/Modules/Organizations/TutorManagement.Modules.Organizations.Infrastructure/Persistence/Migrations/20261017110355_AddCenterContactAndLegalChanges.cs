using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TutorManagement.Modules.Organizations.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCenterContactAndLegalChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "contact_email_verified_at_utc",
                schema: "organization",
                table: "centers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "center_contact_email_changes",
                schema: "organization",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    center_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    new_email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    confirmed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_center_contact_email_changes", x => x.id);
                    table.ForeignKey(
                        name: "FK_center_contact_email_changes_centers_center_id",
                        column: x => x.center_id,
                        principalSchema: "organization",
                        principalTable: "centers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "center_legal_change_requests",
                schema: "organization",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    center_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_legal_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    original_tax_code = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: false),
                    original_business_registration_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    original_registered_address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    proposed_legal_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    proposed_tax_code = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: false),
                    proposed_business_registration_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    proposed_registered_address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    evidence_reference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reviewed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    review_note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_center_legal_change_requests", x => x.id);
                    table.ForeignKey(
                        name: "FK_center_legal_change_requests_centers_center_id",
                        column: x => x.center_id,
                        principalSchema: "organization",
                        principalTable: "centers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "email_outbox_messages",
                schema: "organization",
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

            migrationBuilder.CreateIndex(
                name: "IX_center_contact_email_changes_token_hash",
                schema: "organization",
                table: "center_contact_email_changes",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_center_contact_email_changes_pending",
                schema: "organization",
                table: "center_contact_email_changes",
                column: "center_id",
                unique: true,
                filter: "confirmed_at_utc IS NULL AND revoked_at_utc IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_center_legal_change_requests_center_id_created_at_utc",
                schema: "organization",
                table: "center_legal_change_requests",
                columns: new[] { "center_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ux_center_legal_change_requests_pending",
                schema: "organization",
                table: "center_legal_change_requests",
                column: "center_id",
                unique: true,
                filter: "status = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "IX_email_outbox_messages_processed_at_utc_created_at_utc",
                schema: "organization",
                table: "email_outbox_messages",
                columns: new[] { "processed_at_utc", "created_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "center_contact_email_changes",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "center_legal_change_requests",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "email_outbox_messages",
                schema: "organization");

            migrationBuilder.DropColumn(
                name: "contact_email_verified_at_utc",
                schema: "organization",
                table: "centers");
        }
    }
}
