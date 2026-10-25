using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TutorManagement.Modules.Academics.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCenterSubjects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "academics");

            migrationBuilder.CreateTable(
                name: "subjects",
                schema: "academics",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    center_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    normalized_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    normalized_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    archived_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    archived_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subjects", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_subjects_center_active_name",
                schema: "academics",
                table: "subjects",
                columns: new[] { "center_id", "archived_at_utc", "normalized_name" });

            migrationBuilder.CreateIndex(
                name: "ux_subjects_center_code",
                schema: "academics",
                table: "subjects",
                columns: new[] { "center_id", "normalized_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_subjects_center_name",
                schema: "academics",
                table: "subjects",
                columns: new[] { "center_id", "normalized_name" },
                unique: true);

            // The modules use separate DbContexts, but PostgreSQL can still enforce
            // a real cross-schema relationship in the same database.
            migrationBuilder.AddForeignKey(
                name: "fk_subjects_centers_center_id",
                schema: "academics",
                table: "subjects",
                column: "center_id",
                principalSchema: "organization",
                principalTable: "centers",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "subjects",
                schema: "academics");
        }
    }
}
