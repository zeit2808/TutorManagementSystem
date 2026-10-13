using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TutorManagement.Modules.Organizations.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialOrganizations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(name: "organization");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
