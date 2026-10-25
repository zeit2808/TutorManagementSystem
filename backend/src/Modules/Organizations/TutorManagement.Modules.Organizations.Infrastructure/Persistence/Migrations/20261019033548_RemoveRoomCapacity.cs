using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TutorManagement.Modules.Organizations.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveRoomCapacity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_rooms_capacity",
                schema: "organization",
                table: "rooms");

            migrationBuilder.DropColumn(
                name: "capacity",
                schema: "organization",
                table: "rooms");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "capacity",
                schema: "organization",
                table: "rooms",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddCheckConstraint(
                name: "ck_rooms_capacity",
                schema: "organization",
                table: "rooms",
                sql: "capacity BETWEEN 1 AND 500");
        }
    }
}
