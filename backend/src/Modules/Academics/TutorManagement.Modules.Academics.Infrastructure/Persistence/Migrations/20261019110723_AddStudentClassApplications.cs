using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TutorManagement.Modules.Academics.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStudentClassApplications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "user_id",
                schema: "academics",
                table: "students",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "registration_open",
                schema: "academics",
                table: "classes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "class_applications",
                schema: "academics",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    center_id = table.Column<Guid>(type: "uuid", nullable: false),
                    class_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    submission_id = table.Column<Guid>(type: "uuid", nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    full_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    submitted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reviewed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reviewed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    review_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_class_applications", x => x.id);
                    table.ForeignKey(
                        name: "FK_class_applications_classes_center_id_class_id",
                        columns: x => new { x.center_id, x.class_id },
                        principalSchema: "academics",
                        principalTable: "classes",
                        principalColumns: new[] { "center_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_students_center_user",
                schema: "academics",
                table: "students",
                columns: new[] { "center_id", "user_id" },
                unique: true,
                filter: "user_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_class_applications_center_id_class_id",
                schema: "academics",
                table: "class_applications",
                columns: new[] { "center_id", "class_id" });

            migrationBuilder.CreateIndex(
                name: "IX_class_applications_center_id_status_submitted_at_utc",
                schema: "academics",
                table: "class_applications",
                columns: new[] { "center_id", "status", "submitted_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_class_applications_center_id_user_id_submission_id",
                schema: "academics",
                table: "class_applications",
                columns: new[] { "center_id", "user_id", "submission_id" });

            migrationBuilder.CreateIndex(
                name: "ux_class_applications_active_choice",
                schema: "academics",
                table: "class_applications",
                columns: new[] { "center_id", "user_id", "class_id" },
                unique: true,
                filter: "status IN ('Pending', 'Approved')");

            migrationBuilder.CreateIndex(
                name: "ux_class_applications_idempotency_choice",
                schema: "academics",
                table: "class_applications",
                columns: new[] { "center_id", "user_id", "idempotency_key", "class_id" },
                unique: true);

            // Cross-module relationships still have database-level integrity in the monolith.
            migrationBuilder.AddForeignKey(
                name: "fk_students_users_user_id", schema: "academics", table: "students",
                column: "user_id", principalSchema: "iam", principalTable: "users",
                principalColumn: "id", onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(
                name: "fk_class_applications_users_user_id", schema: "academics", table: "class_applications",
                column: "user_id", principalSchema: "iam", principalTable: "users",
                principalColumn: "id", onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(
                name: "fk_class_applications_reviewer_user_id", schema: "academics", table: "class_applications",
                column: "reviewed_by_user_id", principalSchema: "iam", principalTable: "users",
                principalColumn: "id", onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_class_applications_users_user_id", schema: "academics", table: "class_applications");
            migrationBuilder.DropForeignKey(
                name: "fk_class_applications_reviewer_user_id", schema: "academics", table: "class_applications");
            migrationBuilder.DropForeignKey(
                name: "fk_students_users_user_id", schema: "academics", table: "students");
            migrationBuilder.DropTable(
                name: "class_applications",
                schema: "academics");

            migrationBuilder.DropIndex(
                name: "ux_students_center_user",
                schema: "academics",
                table: "students");

            migrationBuilder.DropColumn(
                name: "user_id",
                schema: "academics",
                table: "students");

            migrationBuilder.DropColumn(
                name: "registration_open",
                schema: "academics",
                table: "classes");
        }
    }
}
