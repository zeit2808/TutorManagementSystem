using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TutorManagement.Modules.Academics.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StudentActivationAndClassInvites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "duplicate_candidate_id",
                schema: "academics",
                table: "students",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "duplicate_reason",
                schema: "academics",
                table: "students",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "merged_into_student_id",
                schema: "academics",
                table: "students",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "class_invite_links",
                schema: "academics",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    center_id = table.Column<Guid>(type: "uuid", nullable: false),
                    class_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    code = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    max_uses = table.Column<int>(type: "integer", nullable: true),
                    use_count = table.Column<int>(type: "integer", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_class_invite_links", x => x.id);
                    table.CheckConstraint("ck_class_invite_links_uses", "use_count >= 0 AND (max_uses IS NULL OR max_uses > 0)");
                    table.ForeignKey(
                        name: "fk_class_invite_links_class",
                        columns: x => new { x.center_id, x.class_id },
                        principalSchema: "academics",
                        principalTable: "classes",
                        principalColumns: new[] { "center_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "student_activation_links",
                schema: "academics",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    center_id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    used_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    used_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revoked_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    declined_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_student_activation_links", x => x.id);
                    table.ForeignKey(
                        name: "fk_student_activation_links_student",
                        columns: x => new { x.center_id, x.student_id },
                        principalSchema: "academics",
                        principalTable: "students",
                        principalColumns: new[] { "center_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "class_invite_attempts",
                schema: "academics",
                columns: table => new
                {
                    link_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    failed_count = table.Column<int>(type: "integer", nullable: false),
                    locked_until_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_class_invite_attempts", x => new { x.link_id, x.user_id });
                    table.ForeignKey(
                        name: "fk_class_invite_attempts_link",
                        column: x => x.link_id,
                        principalSchema: "academics",
                        principalTable: "class_invite_links",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "class_invite_joins",
                schema: "academics",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    center_id = table.Column<Guid>(type: "uuid", nullable: false),
                    link_id = table.Column<Guid>(type: "uuid", nullable: false),
                    class_id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_profile = table.Column<bool>(type: "boolean", nullable: false),
                    had_conflicts = table.Column<bool>(type: "boolean", nullable: false),
                    joined_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_class_invite_joins", x => x.id);
                    table.ForeignKey(
                        name: "fk_class_invite_joins_link",
                        column: x => x.link_id,
                        principalSchema: "academics",
                        principalTable: "class_invite_links",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_class_invite_joins_center",
                schema: "academics",
                table: "class_invite_joins",
                columns: new[] { "center_id", "joined_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_class_invite_joins_link_id",
                schema: "academics",
                table: "class_invite_joins",
                column: "link_id");

            migrationBuilder.CreateIndex(
                name: "ix_class_invite_links_class",
                schema: "academics",
                table: "class_invite_links",
                columns: new[] { "center_id", "class_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ux_class_invite_links_token",
                schema: "academics",
                table: "class_invite_links",
                column: "token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_student_activation_links_student",
                schema: "academics",
                table: "student_activation_links",
                columns: new[] { "center_id", "student_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ux_student_activation_links_token",
                schema: "academics",
                table: "student_activation_links",
                column: "token_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "class_invite_attempts",
                schema: "academics");

            migrationBuilder.DropTable(
                name: "class_invite_joins",
                schema: "academics");

            migrationBuilder.DropTable(
                name: "student_activation_links",
                schema: "academics");

            migrationBuilder.DropTable(
                name: "class_invite_links",
                schema: "academics");

            migrationBuilder.DropColumn(
                name: "duplicate_candidate_id",
                schema: "academics",
                table: "students");

            migrationBuilder.DropColumn(
                name: "duplicate_reason",
                schema: "academics",
                table: "students");

            migrationBuilder.DropColumn(
                name: "merged_into_student_id",
                schema: "academics",
                table: "students");
        }
    }
}
