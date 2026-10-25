using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TutorManagement.Modules.Academics.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StudentContactsWithdrawalReasonsAndCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Decision D9: one counter row per center hands out HS-000001, HS-000002, …
            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS academics.student_code_counters (
                    center_id uuid PRIMARY KEY,
                    last_value integer NOT NULL CHECK (last_value > 0)
                );
                """);

            migrationBuilder.AddColumn<string>(
                name: "parent_name",
                schema: "academics",
                table: "students",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "parent_phone",
                schema: "academics",
                table: "students",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "withdrawal_reason",
                schema: "academics",
                table: "enrollments",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "withdrawn_by_user_id",
                schema: "academics",
                table: "enrollments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "parent_name",
                schema: "academics",
                table: "class_applications",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "parent_phone",
                schema: "academics",
                table: "class_applications",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS academics.student_code_counters;");

            migrationBuilder.DropColumn(
                name: "parent_name",
                schema: "academics",
                table: "students");

            migrationBuilder.DropColumn(
                name: "parent_phone",
                schema: "academics",
                table: "students");

            migrationBuilder.DropColumn(
                name: "withdrawal_reason",
                schema: "academics",
                table: "enrollments");

            migrationBuilder.DropColumn(
                name: "withdrawn_by_user_id",
                schema: "academics",
                table: "enrollments");

            migrationBuilder.DropColumn(
                name: "parent_name",
                schema: "academics",
                table: "class_applications");

            migrationBuilder.DropColumn(
                name: "parent_phone",
                schema: "academics",
                table: "class_applications");
        }
    }
}
