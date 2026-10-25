using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TutorManagement.Modules.Academics.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SessionStatusReasonsAndMakeups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "cancellation_reason",
                schema: "academics",
                table: "teaching_sessions",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "makeup_for_session_id",
                schema: "academics",
                table: "teaching_sessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "not_held_at_utc",
                schema: "academics",
                table: "teaching_sessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "not_held_reason",
                schema: "academics",
                table: "teaching_sessions",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reschedule_reason",
                schema: "academics",
                table: "teaching_sessions",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "status_changed_by_user_id",
                schema: "academics",
                table: "teaching_sessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_teaching_sessions_center_id_makeup_for_session_id",
                schema: "academics",
                table: "teaching_sessions",
                columns: new[] { "center_id", "makeup_for_session_id" });

            migrationBuilder.CreateIndex(
                name: "ux_sessions_makeup_for",
                schema: "academics",
                table: "teaching_sessions",
                column: "makeup_for_session_id",
                unique: true,
                filter: "makeup_for_session_id IS NOT NULL AND cancelled_at_utc IS NULL AND not_held_at_utc IS NULL");

            // A makeup points at another session of the same center, so (center_id, id) must be unique.
            // AddRecurringTeachingSchedules creates that key, but databases migrated with an earlier copy of
            // that migration do not have it; add it only when no unique key on exactly (center_id, id) exists.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1
                        FROM pg_constraint c
                        WHERE c.conrelid = 'academics.teaching_sessions'::regclass
                          AND c.contype IN ('u', 'p')
                          AND (SELECT array_agg(a.attname::text ORDER BY a.attname)
                               FROM unnest(c.conkey) k JOIN pg_attribute a
                                 ON a.attrelid = c.conrelid AND a.attnum = k)
                              = ARRAY['center_id', 'id'])
                    THEN
                        ALTER TABLE academics.teaching_sessions
                            ADD CONSTRAINT "AK_teaching_sessions_center_id_id" UNIQUE (center_id, id);
                    END IF;
                END $$;
                """);

            migrationBuilder.AddForeignKey(
                name: "fk_sessions_makeup_for",
                schema: "academics",
                table: "teaching_sessions",
                columns: new[] { "center_id", "makeup_for_session_id" },
                principalSchema: "academics",
                principalTable: "teaching_sessions",
                principalColumns: new[] { "center_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_sessions_makeup_for",
                schema: "academics",
                table: "teaching_sessions");

            migrationBuilder.DropIndex(
                name: "IX_teaching_sessions_center_id_makeup_for_session_id",
                schema: "academics",
                table: "teaching_sessions");

            migrationBuilder.DropIndex(
                name: "ux_sessions_makeup_for",
                schema: "academics",
                table: "teaching_sessions");

            migrationBuilder.DropColumn(
                name: "cancellation_reason",
                schema: "academics",
                table: "teaching_sessions");

            migrationBuilder.DropColumn(
                name: "makeup_for_session_id",
                schema: "academics",
                table: "teaching_sessions");

            migrationBuilder.DropColumn(
                name: "not_held_at_utc",
                schema: "academics",
                table: "teaching_sessions");

            migrationBuilder.DropColumn(
                name: "not_held_reason",
                schema: "academics",
                table: "teaching_sessions");

            migrationBuilder.DropColumn(
                name: "reschedule_reason",
                schema: "academics",
                table: "teaching_sessions");

            migrationBuilder.DropColumn(
                name: "status_changed_by_user_id",
                schema: "academics",
                table: "teaching_sessions");
        }
    }
}
