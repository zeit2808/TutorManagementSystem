using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TutorManagement.Modules.IdentityAccess.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnforceSingleActiveCenterOwner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ux_center_memberships_active_owner",
                schema: "iam",
                table: "center_memberships",
                column: "user_id",
                unique: true,
                filter: "role = 'CenterOwner' AND is_active");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_center_memberships_active_owner",
                schema: "iam",
                table: "center_memberships");
        }
    }
}
