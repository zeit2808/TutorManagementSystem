using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TutorManagement.Modules.Academics.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRecurringTeachingSchedules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "occurrence_date",
                schema: "academics",
                table: "teaching_sessions",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "rescheduled_at_utc",
                schema: "academics",
                table: "teaching_sessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "series_id",
                schema: "academics",
                table: "teaching_sessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_teaching_sessions_center_id_id",
                schema: "academics",
                table: "teaching_sessions",
                columns: new[] { "center_id", "id" });

            migrationBuilder.CreateTable(
                name: "schedule_series",
                schema: "academics",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    center_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    class_id = table.Column<Guid>(type: "uuid", nullable: false),
                    teacher_id = table.Column<Guid>(type: "uuid", nullable: false),
                    room_id = table.Column<Guid>(type: "uuid", nullable: false),
                    time_zone_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    local_start_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    duration_minutes = table.Column<int>(type: "integer", nullable: false),
                    weekdays_mask = table.Column<int>(type: "integer", nullable: false),
                    excluded_dates = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    replaces_series_id = table.Column<Guid>(type: "uuid", nullable: true),
                    generated_session_count = table.Column<int>(type: "integer", nullable: false),
                    first_start_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_end_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    replaced_sessions_cancelled = table.Column<int>(type: "integer", nullable: false),
                    stopped_from_date = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_schedule_series", x => x.id);
                    table.UniqueConstraint("AK_schedule_series_center_id_id", x => new { x.center_id, x.id });
                    table.CheckConstraint("ck_series_dates", "end_date >= start_date AND end_date - start_date <= 179");
                    table.CheckConstraint("ck_series_duration", "duration_minutes BETWEEN 15 AND 480");
                    table.CheckConstraint("ck_series_weekdays", "weekdays_mask BETWEEN 1 AND 127");
                    table.ForeignKey(
                        name: "FK_schedule_series_classes_center_id_class_id",
                        columns: x => new { x.center_id, x.class_id },
                        principalSchema: "academics",
                        principalTable: "classes",
                        principalColumns: new[] { "center_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_schedule_series_schedule_series_center_id_replaces_series_id",
                        columns: x => new { x.center_id, x.replaces_series_id },
                        principalSchema: "academics",
                        principalTable: "schedule_series",
                        principalColumns: new[] { "center_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_schedule_series_teachers_center_id_teacher_id",
                        columns: x => new { x.center_id, x.teacher_id },
                        principalSchema: "academics",
                        principalTable: "teachers",
                        principalColumns: new[] { "center_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_teaching_sessions_center_id_series_id",
                schema: "academics",
                table: "teaching_sessions",
                columns: new[] { "center_id", "series_id" });

            migrationBuilder.CreateIndex(
                name: "ux_sessions_series_occurrence",
                schema: "academics",
                table: "teaching_sessions",
                columns: new[] { "series_id", "occurrence_date" },
                unique: true,
                filter: "series_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_sessions_series_occurrence",
                schema: "academics",
                table: "teaching_sessions",
                sql: "(series_id IS NULL AND occurrence_date IS NULL) OR (series_id IS NOT NULL AND occurrence_date IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_schedule_series_center_id_class_id_start_date",
                schema: "academics",
                table: "schedule_series",
                columns: new[] { "center_id", "class_id", "start_date" });

            migrationBuilder.CreateIndex(
                name: "IX_schedule_series_center_id_replaces_series_id",
                schema: "academics",
                table: "schedule_series",
                columns: new[] { "center_id", "replaces_series_id" });

            migrationBuilder.CreateIndex(
                name: "IX_schedule_series_center_id_teacher_id",
                schema: "academics",
                table: "schedule_series",
                columns: new[] { "center_id", "teacher_id" });

            migrationBuilder.CreateIndex(
                name: "ux_schedule_series_request",
                schema: "academics",
                table: "schedule_series",
                columns: new[] { "center_id", "request_id" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_teaching_sessions_schedule_series_center_id_series_id",
                schema: "academics",
                table: "teaching_sessions",
                columns: new[] { "center_id", "series_id" },
                principalSchema: "academics",
                principalTable: "schedule_series",
                principalColumns: new[] { "center_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_teaching_sessions_schedule_series_center_id_series_id",
                schema: "academics",
                table: "teaching_sessions");

            migrationBuilder.DropTable(
                name: "schedule_series",
                schema: "academics");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_teaching_sessions_center_id_id",
                schema: "academics",
                table: "teaching_sessions");

            migrationBuilder.DropIndex(
                name: "IX_teaching_sessions_center_id_series_id",
                schema: "academics",
                table: "teaching_sessions");

            migrationBuilder.DropIndex(
                name: "ux_sessions_series_occurrence",
                schema: "academics",
                table: "teaching_sessions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_sessions_series_occurrence",
                schema: "academics",
                table: "teaching_sessions");

            migrationBuilder.DropColumn(
                name: "occurrence_date",
                schema: "academics",
                table: "teaching_sessions");

            migrationBuilder.DropColumn(
                name: "rescheduled_at_utc",
                schema: "academics",
                table: "teaching_sessions");

            migrationBuilder.DropColumn(
                name: "series_id",
                schema: "academics",
                table: "teaching_sessions");
        }
    }
}
