using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TutorManagement.Modules.Academics.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Database backstop for the schedule rules the application already enforces under advisory locks:
    /// an active class, teacher or room cannot have two overlapping sessions. Sessions use half-open
    /// ranges, so back-to-back sessions (one ends 18:00, next starts 18:00) remain valid.
    /// Also adds the btree indexes used by conflict checks and "future sessions" guards.
    /// Nothing here changes the EF model, so the model snapshot is unchanged.
    /// </summary>
    [DbContext(typeof(AcademicsDbContext))]
    [Migration("20261022090000_AddSessionOverlapConstraints")]
    public partial class AddSessionOverlapConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS btree_gist WITH SCHEMA public;");

            // Fail with a readable message instead of a bare exclusion error when old data already overlaps.
            migrationBuilder.Sql("""
                DO $$
                DECLARE overlap_count integer;
                BEGIN
                    SELECT count(*) INTO overlap_count
                    FROM academics.teaching_sessions a
                    JOIN academics.teaching_sessions b
                      ON a.id < b.id
                     AND a.cancelled_at_utc IS NULL AND b.cancelled_at_utc IS NULL
                     AND a.start_utc < b.end_utc AND b.start_utc < a.end_utc
                     AND (a.class_id = b.class_id OR a.teacher_id = b.teacher_id OR a.room_id = b.room_id);
                    IF overlap_count > 0 THEN
                        RAISE EXCEPTION 'Found % overlapping active session pair(s) sharing a class, teacher or room. Cancel or reschedule them before applying AddSessionOverlapConstraints.', overlap_count;
                    END IF;
                END $$;
                """);

            migrationBuilder.Sql("""
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

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS ix_sessions_center_teacher_start
                    ON academics.teaching_sessions (center_id, teacher_id, start_utc);
                CREATE INDEX IF NOT EXISTS ix_sessions_center_room_start
                    ON academics.teaching_sessions (center_id, room_id, start_utc);
                CREATE INDEX IF NOT EXISTS ix_sessions_center_class_start
                    ON academics.teaching_sessions (center_id, class_id, start_utc);
                CREATE INDEX IF NOT EXISTS ix_enrollments_student
                    ON academics.enrollments (student_id);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS academics.ix_enrollments_student;
                DROP INDEX IF EXISTS academics.ix_sessions_center_class_start;
                DROP INDEX IF EXISTS academics.ix_sessions_center_room_start;
                DROP INDEX IF EXISTS academics.ix_sessions_center_teacher_start;
                ALTER TABLE academics.teaching_sessions DROP CONSTRAINT IF EXISTS ex_sessions_room_overlap;
                ALTER TABLE academics.teaching_sessions DROP CONSTRAINT IF EXISTS ex_sessions_teacher_overlap;
                ALTER TABLE academics.teaching_sessions DROP CONSTRAINT IF EXISTS ex_sessions_class_overlap;
                """);
            // btree_gist is left installed: other schemas may depend on it.
        }
    }
}
