using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TutorManagement.Modules.Academics.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialAcademics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(name: "academics");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
