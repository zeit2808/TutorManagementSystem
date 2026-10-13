using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TutorManagement.Modules.IdentityAccess.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialIdentityAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(name: "iam");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
