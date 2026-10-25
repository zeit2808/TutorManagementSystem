using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TutorManagement.Modules.Academics.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionAttendance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_teaching_sessions_center_id_id_class_id",
                schema: "academics",
                table: "teaching_sessions",
                columns: new[] { "center_id", "id", "class_id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_enrollments_center_id_id_student_id",
                schema: "academics",
                table: "enrollments",
                columns: new[] { "center_id", "id", "student_id" });

            migrationBuilder.CreateTable(
                name: "attendance_sheets",
                schema: "academics",
                columns: table => new
                {
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    center_id = table.Column<Guid>(type: "uuid", nullable: false),
                    class_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    actual_teacher_id = table.Column<Guid>(type: "uuid", nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    submitted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    submitted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_attendance_sheets", x => x.session_id);
                    table.UniqueConstraint("AK_attendance_sheets_center_id_session_id", x => new { x.center_id, x.session_id });
                    table.CheckConstraint("ck_attendance_version", "version > 0");
                    table.ForeignKey(
                        name: "FK_attendance_sheets_teachers_center_id_actual_teacher_id",
                        columns: x => new { x.center_id, x.actual_teacher_id },
                        principalSchema: "academics",
                        principalTable: "teachers",
                        principalColumns: new[] { "center_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_attendance_sheets_teaching_sessions_center_id_session_id_cl~",
                        columns: x => new { x.center_id, x.session_id, x.class_id },
                        principalSchema: "academics",
                        principalTable: "teaching_sessions",
                        principalColumns: new[] { "center_id", "id", "class_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "attendance_entries",
                schema: "academics",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    center_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    enrollment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mark = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_attendance_entries", x => x.id);
                    table.ForeignKey(
                        name: "FK_attendance_entries_attendance_sheets_center_id_session_id",
                        columns: x => new { x.center_id, x.session_id },
                        principalSchema: "academics",
                        principalTable: "attendance_sheets",
                        principalColumns: new[] { "center_id", "session_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_attendance_entries_enrollments_center_id_enrollment_id_stud~",
                        columns: x => new { x.center_id, x.enrollment_id, x.student_id },
                        principalSchema: "academics",
                        principalTable: "enrollments",
                        principalColumns: new[] { "center_id", "id", "student_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_attendance_entries_students_center_id_student_id",
                        columns: x => new { x.center_id, x.student_id },
                        principalSchema: "academics",
                        principalTable: "students",
                        principalColumns: new[] { "center_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "attendance_revisions",
                schema: "academics",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    center_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    action = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    before_json = table.Column<string>(type: "jsonb", nullable: false),
                    after_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_attendance_revisions", x => x.id);
                    table.ForeignKey(
                        name: "FK_attendance_revisions_attendance_sheets_center_id_session_id",
                        columns: x => new { x.center_id, x.session_id },
                        principalSchema: "academics",
                        principalTable: "attendance_sheets",
                        principalColumns: new[] { "center_id", "session_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_attendance_entries_center_id_enrollment_id_student_id",
                schema: "academics",
                table: "attendance_entries",
                columns: new[] { "center_id", "enrollment_id", "student_id" });

            migrationBuilder.CreateIndex(
                name: "IX_attendance_entries_center_id_student_id",
                schema: "academics",
                table: "attendance_entries",
                columns: new[] { "center_id", "student_id" });

            migrationBuilder.CreateIndex(
                name: "ux_attendance_student_session",
                schema: "academics",
                table: "attendance_entries",
                columns: new[] { "center_id", "session_id", "student_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_attendance_revision_version",
                schema: "academics",
                table: "attendance_revisions",
                columns: new[] { "center_id", "session_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_attendance_sheets_center_id_actual_teacher_id",
                schema: "academics",
                table: "attendance_sheets",
                columns: new[] { "center_id", "actual_teacher_id" });

            migrationBuilder.CreateIndex(
                name: "IX_attendance_sheets_center_id_class_id_status",
                schema: "academics",
                table: "attendance_sheets",
                columns: new[] { "center_id", "class_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_attendance_sheets_center_id_session_id_class_id",
                schema: "academics",
                table: "attendance_sheets",
                columns: new[] { "center_id", "session_id", "class_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "attendance_entries",
                schema: "academics");

            migrationBuilder.DropTable(
                name: "attendance_revisions",
                schema: "academics");

            migrationBuilder.DropTable(
                name: "attendance_sheets",
                schema: "academics");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_teaching_sessions_center_id_id_class_id",
                schema: "academics",
                table: "teaching_sessions");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_enrollments_center_id_id_student_id",
                schema: "academics",
                table: "enrollments");
        }
    }
}
