using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TutorManagement.Modules.IdentityAccess.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Student accounts and staff accounts are now separate kinds. Until now, linking Google to a staff account
    /// also granted the Student role, and a Google student account could accept a staff invitation. Staff wins:
    /// any account that holds a center membership (active or not) or the PlatformAdmin role loses the Student
    /// role. Data in centers (student profiles, applications) is left untouched. Nothing here changes the EF
    /// model, so the model snapshot is unchanged. Down is a no-op: the removed rows cannot be told apart later.
    /// </summary>
    [DbContext(typeof(IdentityDbContext))]
    [Migration("20261022180000_SeparateStudentAndStaffAccounts")]
    public partial class SeparateStudentAndStaffAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM iam.user_roles ur
                WHERE ur.role_id = '10000000-0000-0000-0000-000000000004'
                  AND (EXISTS (SELECT 1 FROM iam.center_memberships m WHERE m.user_id = ur.user_id)
                       OR EXISTS (SELECT 1 FROM iam.user_roles admin
                                  WHERE admin.user_id = ur.user_id
                                    AND admin.role_id = '10000000-0000-0000-0000-000000000001'));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
