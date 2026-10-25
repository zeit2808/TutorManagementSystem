using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TutorManagement.Modules.Organizations.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CenterLifecycleAndDirectory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "listed_in_directory",
                schema: "organization",
                table: "centers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "center_status_history",
                schema: "organization",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    center_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    to_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    changed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_center_status_history", x => x.id);
                    table.ForeignKey(
                        name: "FK_center_status_history_centers_center_id",
                        column: x => x.center_id,
                        principalSchema: "organization",
                        principalTable: "centers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_center_status_history_center",
                schema: "organization",
                table: "center_status_history",
                columns: new[] { "center_id", "changed_at_utc" });

            // Existing centers get the history they would have had: the application, then the last review.
            migrationBuilder.Sql("""
                INSERT INTO organization.center_status_history (id, center_id, from_status, to_status, actor_user_id, note, changed_at_utc)
                SELECT gen_random_uuid(), id, NULL, 'PendingApproval', NULL, NULL, created_at_utc
                FROM organization.centers;

                INSERT INTO organization.center_status_history (id, center_id, from_status, to_status, actor_user_id, note, changed_at_utc)
                SELECT gen_random_uuid(), id, 'PendingApproval', status, reviewed_by_user_id, review_note,
                       COALESCE(reviewed_at_utc, updated_at_utc)
                FROM organization.centers
                WHERE status <> 'PendingApproval';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "center_status_history",
                schema: "organization");

            migrationBuilder.DropColumn(
                name: "listed_in_directory",
                schema: "organization",
                table: "centers");
        }
    }
}
