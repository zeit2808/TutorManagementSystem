using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TutorManagement.Infrastructure.Persistence;

#nullable disable

namespace TutorManagement.Modules.Academics.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// D9 for every catalog: one counter table per center and code kind (HS, GV, LH, MH) replaces the
    /// student-only counter. The HS counter keeps its position so no student code is handed out twice.
    /// </summary>
    [DbContext(typeof(AcademicsDbContext))]
    [Migration("20261026120000_GeneratedEntityCodes")]
    public partial class GeneratedEntityCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(CodeSequence.CreateCountersTableSql(AcademicsDbContext.Schema));
            migrationBuilder.Sql("""
                INSERT INTO academics.code_counters (center_id, kind, last_value)
                SELECT center_id, 'HS', last_value FROM academics.student_code_counters
                ON CONFLICT (center_id, kind) DO UPDATE
                    SET last_value = GREATEST(academics.code_counters.last_value, EXCLUDED.last_value);
                DROP TABLE IF EXISTS academics.student_code_counters;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS academics.student_code_counters (
                    center_id uuid PRIMARY KEY,
                    last_value integer NOT NULL CHECK (last_value > 0)
                );
                INSERT INTO academics.student_code_counters (center_id, last_value)
                SELECT center_id, last_value::integer FROM academics.code_counters WHERE kind = 'HS'
                ON CONFLICT (center_id) DO NOTHING;
                """);
            migrationBuilder.Sql(CodeSequence.DropCountersTableSql(AcademicsDbContext.Schema));
        }
    }
}
