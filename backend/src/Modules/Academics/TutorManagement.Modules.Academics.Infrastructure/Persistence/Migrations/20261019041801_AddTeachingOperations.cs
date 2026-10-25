using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TutorManagement.Modules.Academics.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTeachingOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_subjects_center_id_id",
                schema: "academics",
                table: "subjects",
                columns: new[] { "center_id", "id" });

            migrationBuilder.CreateTable(
                name: "classes",
                schema: "academics",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    center_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    max_students = table.Column<int>(type: "integer", nullable: false),
                    enrolled_count = table.Column<int>(type: "integer", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    archived_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_classes", x => x.id);
                    table.UniqueConstraint("AK_classes_center_id_id", x => new { x.center_id, x.id });
                    table.CheckConstraint("ck_classes_enrolled_count", "enrolled_count BETWEEN 0 AND max_students");
                    table.CheckConstraint("ck_classes_max_students", "max_students BETWEEN 1 AND 500");
                    table.ForeignKey(
                        name: "FK_classes_subjects_center_id_subject_id",
                        columns: x => new { x.center_id, x.subject_id },
                        principalSchema: "academics",
                        principalTable: "subjects",
                        principalColumns: new[] { "center_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "students",
                schema: "academics",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    center_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    full_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    archived_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_students", x => x.id);
                    table.UniqueConstraint("AK_students_center_id_id", x => new { x.center_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "teachers",
                schema: "academics",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    center_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    full_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    archived_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_teachers", x => x.id);
                    table.UniqueConstraint("AK_teachers_center_id_id", x => new { x.center_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "enrollments",
                schema: "academics",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    center_id = table.Column<Guid>(type: "uuid", nullable: false),
                    class_id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_id = table.Column<Guid>(type: "uuid", nullable: false),
                    enrolled_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    withdrawn_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_enrollments", x => x.id);
                    table.ForeignKey(
                        name: "FK_enrollments_classes_center_id_class_id",
                        columns: x => new { x.center_id, x.class_id },
                        principalSchema: "academics",
                        principalTable: "classes",
                        principalColumns: new[] { "center_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_enrollments_students_center_id_student_id",
                        columns: x => new { x.center_id, x.student_id },
                        principalSchema: "academics",
                        principalTable: "students",
                        principalColumns: new[] { "center_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "teaching_sessions",
                schema: "academics",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    center_id = table.Column<Guid>(type: "uuid", nullable: false),
                    class_id = table.Column<Guid>(type: "uuid", nullable: false),
                    teacher_id = table.Column<Guid>(type: "uuid", nullable: false),
                    room_id = table.Column<Guid>(type: "uuid", nullable: false),
                    start_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    end_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    cancelled_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_teaching_sessions", x => x.id);
                    table.CheckConstraint("ck_sessions_time", "end_utc > start_utc");
                    table.ForeignKey(
                        name: "FK_teaching_sessions_classes_center_id_class_id",
                        columns: x => new { x.center_id, x.class_id },
                        principalSchema: "academics",
                        principalTable: "classes",
                        principalColumns: new[] { "center_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_teaching_sessions_teachers_center_id_teacher_id",
                        columns: x => new { x.center_id, x.teacher_id },
                        principalSchema: "academics",
                        principalTable: "teachers",
                        principalColumns: new[] { "center_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_classes_center_id_subject_id",
                schema: "academics",
                table: "classes",
                columns: new[] { "center_id", "subject_id" });

            migrationBuilder.CreateIndex(
                name: "ux_classes_center_code",
                schema: "academics",
                table: "classes",
                columns: new[] { "center_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_enrollments_center_id_class_id",
                schema: "academics",
                table: "enrollments",
                columns: new[] { "center_id", "class_id" });

            migrationBuilder.CreateIndex(
                name: "IX_enrollments_center_id_student_id_withdrawn_at_utc",
                schema: "academics",
                table: "enrollments",
                columns: new[] { "center_id", "student_id", "withdrawn_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ux_enrollments_active_class_student",
                schema: "academics",
                table: "enrollments",
                columns: new[] { "class_id", "student_id" },
                unique: true,
                filter: "withdrawn_at_utc IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_students_center_code",
                schema: "academics",
                table: "students",
                columns: new[] { "center_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_teachers_center_code",
                schema: "academics",
                table: "teachers",
                columns: new[] { "center_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_teaching_sessions_center_id_class_id",
                schema: "academics",
                table: "teaching_sessions",
                columns: new[] { "center_id", "class_id" });

            migrationBuilder.CreateIndex(
                name: "IX_teaching_sessions_center_id_start_utc",
                schema: "academics",
                table: "teaching_sessions",
                columns: new[] { "center_id", "start_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_teaching_sessions_center_id_teacher_id",
                schema: "academics",
                table: "teaching_sessions",
                columns: new[] { "center_id", "teacher_id" });

            // These are owned by other modules, but PostgreSQL still enforces the
            // references (including the center boundary) within this monolith DB.
            migrationBuilder.AddForeignKey(
                name: "fk_students_centers_center_id", schema: "academics", table: "students",
                column: "center_id", principalSchema: "organization", principalTable: "centers",
                principalColumn: "id", onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(
                name: "fk_teachers_centers_center_id", schema: "academics", table: "teachers",
                column: "center_id", principalSchema: "organization", principalTable: "centers",
                principalColumn: "id", onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(
                name: "fk_sessions_rooms_center_id_room_id", schema: "academics", table: "teaching_sessions",
                columns: new[] { "center_id", "room_id" }, principalSchema: "organization", principalTable: "rooms",
                principalColumns: new[] { "center_id", "id" }, onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "enrollments",
                schema: "academics");

            migrationBuilder.DropTable(
                name: "teaching_sessions",
                schema: "academics");

            migrationBuilder.DropTable(
                name: "students",
                schema: "academics");

            migrationBuilder.DropTable(
                name: "classes",
                schema: "academics");

            migrationBuilder.DropTable(
                name: "teachers",
                schema: "academics");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_subjects_center_id_id",
                schema: "academics",
                table: "subjects");
        }
    }
}
