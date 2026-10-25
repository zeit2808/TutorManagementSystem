using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TutorManagement.Modules.Academics.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ScheduleChangesNoticesAndRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "schedule_changes",
                schema: "academics",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    center_id = table.Column<Guid>(type: "uuid", nullable: false),
                    class_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    series_id = table.Column<Guid>(type: "uuid", nullable: true),
                    old_start_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    old_end_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    new_start_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    new_end_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    old_teacher_id = table.Column<Guid>(type: "uuid", nullable: true),
                    new_teacher_id = table.Column<Guid>(type: "uuid", nullable: true),
                    old_room_id = table.Column<Guid>(type: "uuid", nullable: true),
                    new_room_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_urgent = table.Column<bool>(type: "boolean", nullable: false),
                    affected_session_count = table.Column<int>(type: "integer", nullable: false),
                    first_affected_start_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_schedule_changes", x => x.id);
                    table.UniqueConstraint("AK_schedule_changes_center_id_id", x => new { x.center_id, x.id });
                    table.ForeignKey(
                        name: "fk_schedule_changes_class",
                        columns: x => new { x.center_id, x.class_id },
                        principalSchema: "academics",
                        principalTable: "classes",
                        principalColumns: new[] { "center_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_schedule_changes_session",
                        columns: x => new { x.center_id, x.session_id },
                        principalSchema: "academics",
                        principalTable: "teaching_sessions",
                        principalColumns: new[] { "center_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "schedule_notices",
                schema: "academics",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    center_id = table.Column<Guid>(type: "uuid", nullable: false),
                    change_id = table.Column<Guid>(type: "uuid", nullable: false),
                    class_id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_urgent = table.Column<bool>(type: "boolean", nullable: false),
                    conflict_count = table.Column<int>(type: "integer", nullable: false),
                    email_queued = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    seen_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    response = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    responded_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_schedule_notices", x => x.id);
                    table.UniqueConstraint("AK_schedule_notices_center_id_id", x => new { x.center_id, x.id });
                    table.ForeignKey(
                        name: "fk_schedule_notices_change",
                        columns: x => new { x.center_id, x.change_id },
                        principalSchema: "academics",
                        principalTable: "schedule_changes",
                        principalColumns: new[] { "center_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_schedule_notices_student",
                        columns: x => new { x.center_id, x.student_id },
                        principalSchema: "academics",
                        principalTable: "students",
                        principalColumns: new[] { "center_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "student_requests",
                schema: "academics",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    center_id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    class_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    target_class_id = table.Column<Guid>(type: "uuid", nullable: true),
                    notice_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    submitted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reviewed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reviewed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    review_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_student_requests", x => x.id);
                    table.UniqueConstraint("AK_student_requests_center_id_id", x => new { x.center_id, x.id });
                    table.CheckConstraint("ck_student_requests_session", "(kind IN ('ExcusedAbsence','Makeup')) = (session_id IS NOT NULL)");
                    table.CheckConstraint("ck_student_requests_target", "(kind = 'Transfer') = (target_class_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_student_requests_class",
                        columns: x => new { x.center_id, x.class_id },
                        principalSchema: "academics",
                        principalTable: "classes",
                        principalColumns: new[] { "center_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_student_requests_notice",
                        columns: x => new { x.center_id, x.notice_id },
                        principalSchema: "academics",
                        principalTable: "schedule_notices",
                        principalColumns: new[] { "center_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_student_requests_session",
                        columns: x => new { x.center_id, x.session_id },
                        principalSchema: "academics",
                        principalTable: "teaching_sessions",
                        principalColumns: new[] { "center_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_student_requests_student",
                        columns: x => new { x.center_id, x.student_id },
                        principalSchema: "academics",
                        principalTable: "students",
                        principalColumns: new[] { "center_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_student_requests_target_class",
                        columns: x => new { x.center_id, x.target_class_id },
                        principalSchema: "academics",
                        principalTable: "classes",
                        principalColumns: new[] { "center_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_schedule_changes_center_created",
                schema: "academics",
                table: "schedule_changes",
                columns: new[] { "center_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_schedule_changes_center_id_class_id",
                schema: "academics",
                table: "schedule_changes",
                columns: new[] { "center_id", "class_id" });

            migrationBuilder.CreateIndex(
                name: "IX_schedule_changes_center_id_session_id",
                schema: "academics",
                table: "schedule_changes",
                columns: new[] { "center_id", "session_id" });

            migrationBuilder.CreateIndex(
                name: "ix_schedule_changes_session",
                schema: "academics",
                table: "schedule_changes",
                columns: new[] { "session_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_schedule_notices_center_id_change_id",
                schema: "academics",
                table: "schedule_notices",
                columns: new[] { "center_id", "change_id" });

            migrationBuilder.CreateIndex(
                name: "IX_schedule_notices_center_id_student_id",
                schema: "academics",
                table: "schedule_notices",
                columns: new[] { "center_id", "student_id" });

            migrationBuilder.CreateIndex(
                name: "ix_schedule_notices_user",
                schema: "academics",
                table: "schedule_notices",
                columns: new[] { "user_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ux_schedule_notices_change_student",
                schema: "academics",
                table: "schedule_notices",
                columns: new[] { "change_id", "student_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_student_requests_center_id_class_id",
                schema: "academics",
                table: "student_requests",
                columns: new[] { "center_id", "class_id" });

            migrationBuilder.CreateIndex(
                name: "IX_student_requests_center_id_notice_id",
                schema: "academics",
                table: "student_requests",
                columns: new[] { "center_id", "notice_id" });

            migrationBuilder.CreateIndex(
                name: "IX_student_requests_center_id_session_id",
                schema: "academics",
                table: "student_requests",
                columns: new[] { "center_id", "session_id" });

            migrationBuilder.CreateIndex(
                name: "IX_student_requests_center_id_student_id",
                schema: "academics",
                table: "student_requests",
                columns: new[] { "center_id", "student_id" });

            migrationBuilder.CreateIndex(
                name: "IX_student_requests_center_id_target_class_id",
                schema: "academics",
                table: "student_requests",
                columns: new[] { "center_id", "target_class_id" });

            migrationBuilder.CreateIndex(
                name: "ix_student_requests_center_status",
                schema: "academics",
                table: "student_requests",
                columns: new[] { "center_id", "status", "submitted_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_student_requests_user",
                schema: "academics",
                table: "student_requests",
                columns: new[] { "user_id", "submitted_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ux_student_requests_pending_class",
                schema: "academics",
                table: "student_requests",
                columns: new[] { "student_id", "kind", "class_id" },
                unique: true,
                filter: "status = 'Pending' AND session_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_student_requests_pending_session",
                schema: "academics",
                table: "student_requests",
                columns: new[] { "student_id", "kind", "session_id" },
                unique: true,
                filter: "status = 'Pending' AND session_id IS NOT NULL");

            // A session recorded as "not held" no longer occupies its class, teacher or room, so a makeup
            // can use the same slot. Rebuild the overlap backstops to ignore it like cancelled sessions.
            migrationBuilder.Sql("""
                ALTER TABLE academics.teaching_sessions DROP CONSTRAINT IF EXISTS ex_sessions_class_overlap;
                ALTER TABLE academics.teaching_sessions DROP CONSTRAINT IF EXISTS ex_sessions_teacher_overlap;
                ALTER TABLE academics.teaching_sessions DROP CONSTRAINT IF EXISTS ex_sessions_room_overlap;
                ALTER TABLE academics.teaching_sessions
                    ADD CONSTRAINT ex_sessions_class_overlap
                    EXCLUDE USING gist (class_id WITH =, tstzrange(start_utc, end_utc, '[)') WITH &&)
                    WHERE (cancelled_at_utc IS NULL AND not_held_at_utc IS NULL);
                ALTER TABLE academics.teaching_sessions
                    ADD CONSTRAINT ex_sessions_teacher_overlap
                    EXCLUDE USING gist (teacher_id WITH =, tstzrange(start_utc, end_utc, '[)') WITH &&)
                    WHERE (cancelled_at_utc IS NULL AND not_held_at_utc IS NULL);
                ALTER TABLE academics.teaching_sessions
                    ADD CONSTRAINT ex_sessions_room_overlap
                    EXCLUDE USING gist (room_id WITH =, tstzrange(start_utc, end_utc, '[)') WITH &&)
                    WHERE (cancelled_at_utc IS NULL AND not_held_at_utc IS NULL);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE academics.teaching_sessions DROP CONSTRAINT IF EXISTS ex_sessions_class_overlap;
                ALTER TABLE academics.teaching_sessions DROP CONSTRAINT IF EXISTS ex_sessions_teacher_overlap;
                ALTER TABLE academics.teaching_sessions DROP CONSTRAINT IF EXISTS ex_sessions_room_overlap;
                ALTER TABLE academics.teaching_sessions
                    ADD CONSTRAINT ex_sessions_class_overlap
                    EXCLUDE USING gist (class_id WITH =, tstzrange(start_utc, end_utc, '[)') WITH &&)
                    WHERE (cancelled_at_utc IS NULL);
                ALTER TABLE academics.teaching_sessions
                    ADD CONSTRAINT ex_sessions_teacher_overlap
                    EXCLUDE USING gist (teacher_id WITH =, tstzrange(start_utc, end_utc, '[)') WITH &&)
                    WHERE (cancelled_at_utc IS NULL);
                ALTER TABLE academics.teaching_sessions
                    ADD CONSTRAINT ex_sessions_room_overlap
                    EXCLUDE USING gist (room_id WITH =, tstzrange(start_utc, end_utc, '[)') WITH &&)
                    WHERE (cancelled_at_utc IS NULL);
                """);

            migrationBuilder.DropTable(
                name: "student_requests",
                schema: "academics");

            migrationBuilder.DropTable(
                name: "schedule_notices",
                schema: "academics");

            migrationBuilder.DropTable(
                name: "schedule_changes",
                schema: "academics");
        }
    }
}
