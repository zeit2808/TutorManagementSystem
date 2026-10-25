using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TutorManagement.Modules.Organizations.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRoomCenterReferenceKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_rooms_center_id_id",
                schema: "organization",
                table: "rooms",
                columns: new[] { "center_id", "id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropUniqueConstraint(
                name: "AK_rooms_center_id_id",
                schema: "organization",
                table: "rooms");
        }
    }
}
