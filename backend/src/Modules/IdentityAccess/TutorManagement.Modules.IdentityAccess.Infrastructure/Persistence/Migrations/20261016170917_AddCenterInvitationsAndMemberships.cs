using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TutorManagement.Modules.IdentityAccess.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCenterInvitationsAndMemberships : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "center_invitations",
                schema: "iam",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    center_id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    normalized_email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    role = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    invited_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    accepted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    accepted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revoked_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revocation_reason = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_center_invitations", x => x.id);
                    table.ForeignKey(
                        name: "FK_center_invitations_users_accepted_by_user_id",
                        column: x => x.accepted_by_user_id,
                        principalSchema: "iam",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_center_invitations_users_invited_by_user_id",
                        column: x => x.invited_by_user_id,
                        principalSchema: "iam",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "center_memberships",
                schema: "iam",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    center_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_center_memberships", x => x.id);
                    table.ForeignKey(
                        name: "FK_center_memberships_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "iam",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            // The modular monolith uses separate schemas in one PostgreSQL database.
            // These database-level foreign keys preserve referential integrity across modules.
            migrationBuilder.AddForeignKey(
                name: "FK_center_invitations_centers_center_id",
                schema: "iam",
                table: "center_invitations",
                column: "center_id",
                principalSchema: "organization",
                principalTable: "centers",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_center_memberships_centers_center_id",
                schema: "iam",
                table: "center_memberships",
                column: "center_id",
                principalSchema: "organization",
                principalTable: "centers",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.UpdateData(
                schema: "iam",
                table: "roles",
                keyColumn: "id",
                keyValue: new Guid("10000000-0000-0000-0000-000000000001"),
                columns: new[] { "name", "normalized_name" },
                values: new object[] { "PlatformAdmin", "PLATFORMADMIN" });

            migrationBuilder.CreateIndex(
                name: "IX_center_invitations_accepted_by_user_id",
                schema: "iam",
                table: "center_invitations",
                column: "accepted_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_center_invitations_center_email",
                schema: "iam",
                table: "center_invitations",
                columns: new[] { "center_id", "normalized_email" });

            migrationBuilder.CreateIndex(
                name: "IX_center_invitations_invited_by_user_id",
                schema: "iam",
                table: "center_invitations",
                column: "invited_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ux_center_invitations_token_hash",
                schema: "iam",
                table: "center_invitations",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_center_memberships_user_id",
                schema: "iam",
                table: "center_memberships",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ux_center_memberships_center_user",
                schema: "iam",
                table: "center_memberships",
                columns: new[] { "center_id", "user_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "center_invitations",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "center_memberships",
                schema: "iam");

            migrationBuilder.UpdateData(
                schema: "iam",
                table: "roles",
                keyColumn: "id",
                keyValue: new Guid("10000000-0000-0000-0000-000000000001"),
                columns: new[] { "name", "normalized_name" },
                values: new object[] { "Owner", "OWNER" });
        }
    }
}
