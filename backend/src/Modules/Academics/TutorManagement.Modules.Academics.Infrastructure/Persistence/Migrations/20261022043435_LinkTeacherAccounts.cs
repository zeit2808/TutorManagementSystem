using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TutorManagement.Modules.Academics.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LinkTeacherAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "user_id",
                schema: "academics",
                table: "teachers",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_teachers_center_user",
                schema: "academics",
                table: "teachers",
                columns: new[] { "center_id", "user_id" },
                unique: true,
                filter: "user_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_teachers_center_user",
                schema: "academics",
                table: "teachers");

            migrationBuilder.DropColumn(
                name: "user_id",
                schema: "academics",
                table: "teachers");
        }
    }
}
