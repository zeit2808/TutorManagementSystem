using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TutorManagement.Modules.Organizations.Infrastructure.Persistence.Migrations;

public partial class UnifyCenterChangeRequests : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Keep old email-change and outbox rows as read-only history. No active code
        // writes or delivers messages from these tables after this migration.
        migrationBuilder.RenameTable(
            name: "center_contact_email_changes", schema: "organization",
            newName: "legacy_center_contact_email_changes", newSchema: "organization");
        migrationBuilder.RenameTable(
            name: "email_outbox_messages", schema: "organization",
            newName: "legacy_email_outbox_messages", newSchema: "organization");

        migrationBuilder.RenameTable(
            name: "center_legal_change_requests", schema: "organization",
            newName: "center_change_requests", newSchema: "organization");
        migrationBuilder.RenameIndex(
            name: "IX_center_legal_change_requests_center_id_created_at_utc",
            schema: "organization", table: "center_change_requests",
            newName: "IX_center_change_requests_center_id_created_at_utc");
        migrationBuilder.RenameIndex(
            name: "ux_center_legal_change_requests_pending",
            schema: "organization", table: "center_change_requests",
            newName: "ux_center_change_requests_pending");
        migrationBuilder.Sql("""
            ALTER TABLE organization.center_change_requests
              RENAME CONSTRAINT "PK_center_legal_change_requests" TO "PK_center_change_requests";
            ALTER TABLE organization.center_change_requests
              RENAME CONSTRAINT "FK_center_legal_change_requests_centers_center_id"
              TO "FK_center_change_requests_centers_center_id";
            """);

        migrationBuilder.AddColumn<string>(
            name: "original_display_name", schema: "organization", table: "center_change_requests",
            type: "character varying(150)", maxLength: 150, nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "original_contact_email", schema: "organization", table: "center_change_requests",
            type: "character varying(254)", maxLength: 254, nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "original_contact_phone", schema: "organization", table: "center_change_requests",
            type: "character varying(20)", maxLength: 20, nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "proposed_display_name", schema: "organization", table: "center_change_requests",
            type: "character varying(150)", maxLength: 150, nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "proposed_contact_email", schema: "organization", table: "center_change_requests",
            type: "character varying(254)", maxLength: 254, nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "proposed_contact_phone", schema: "organization", table: "center_change_requests",
            type: "character varying(20)", maxLength: 20, nullable: true);

        migrationBuilder.Sql("""
            UPDATE organization.center_change_requests AS request
            SET original_display_name = center.display_name,
                original_contact_email = center.contact_email,
                original_contact_phone = center.contact_phone,
                proposed_display_name = center.display_name,
                proposed_contact_email = center.contact_email,
                proposed_contact_phone = center.contact_phone
            FROM organization.centers AS center
            WHERE request.center_id = center.id;
            """);

        foreach (var (column, length) in new[]
        {
            ("original_display_name", 150), ("original_contact_email", 254),
            ("original_contact_phone", 20), ("proposed_display_name", 150),
            ("proposed_contact_email", 254), ("proposed_contact_phone", 20)
        })
        {
            migrationBuilder.AlterColumn<string>(
                name: column, schema: "organization", table: "center_change_requests",
                type: $"character varying({length})", maxLength: length, nullable: false,
                oldClrType: typeof(string), oldType: $"character varying({length})",
                oldMaxLength: length, oldNullable: true);
        }
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        foreach (var column in new[]
        {
            "original_display_name", "original_contact_email", "original_contact_phone",
            "proposed_display_name", "proposed_contact_email", "proposed_contact_phone"
        })
        {
            migrationBuilder.DropColumn(
                name: column, schema: "organization", table: "center_change_requests");
        }

        migrationBuilder.Sql("""
            ALTER TABLE organization.center_change_requests
              RENAME CONSTRAINT "FK_center_change_requests_centers_center_id"
              TO "FK_center_legal_change_requests_centers_center_id";
            ALTER TABLE organization.center_change_requests
              RENAME CONSTRAINT "PK_center_change_requests" TO "PK_center_legal_change_requests";
            """);
        migrationBuilder.RenameIndex(
            name: "IX_center_change_requests_center_id_created_at_utc",
            schema: "organization", table: "center_change_requests",
            newName: "IX_center_legal_change_requests_center_id_created_at_utc");
        migrationBuilder.RenameIndex(
            name: "ux_center_change_requests_pending",
            schema: "organization", table: "center_change_requests",
            newName: "ux_center_legal_change_requests_pending");
        migrationBuilder.RenameTable(
            name: "center_change_requests", schema: "organization",
            newName: "center_legal_change_requests", newSchema: "organization");
        migrationBuilder.RenameTable(
            name: "legacy_center_contact_email_changes", schema: "organization",
            newName: "center_contact_email_changes", newSchema: "organization");
        migrationBuilder.RenameTable(
            name: "legacy_email_outbox_messages", schema: "organization",
            newName: "email_outbox_messages", newSchema: "organization");
    }
}
