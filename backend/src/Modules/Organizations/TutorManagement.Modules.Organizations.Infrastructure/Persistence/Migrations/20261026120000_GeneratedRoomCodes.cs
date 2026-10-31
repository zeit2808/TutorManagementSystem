using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TutorManagement.Infrastructure.Persistence;

#nullable disable

namespace TutorManagement.Modules.Organizations.Infrastructure.Persistence.Migrations
{
    /// <summary>D9 for rooms: the counter table behind the generated PH-000001 codes.</summary>
    [DbContext(typeof(OrganizationDbContext))]
    [Migration("20261026120000_GeneratedRoomCodes")]
    public partial class GeneratedRoomCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(CodeSequence.CreateCountersTableSql(OrganizationDbContext.Schema));

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(CodeSequence.DropCountersTableSql(OrganizationDbContext.Schema));
    }
}
