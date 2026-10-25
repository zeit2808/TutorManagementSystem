using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TutorManagement.Modules.Organizations.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCenterRooms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "rooms",
                schema: "organization",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    center_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    normalized_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    normalized_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    capacity = table.Column<int>(type: "integer", nullable: false),
                    location_note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    archived_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    archived_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rooms", x => x.id);
                    table.CheckConstraint("ck_rooms_capacity", "capacity BETWEEN 1 AND 500");
                    table.ForeignKey(
                        name: "FK_rooms_centers_center_id",
                        column: x => x.center_id,
                        principalSchema: "organization",
                        principalTable: "centers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_rooms_center_active_name",
                schema: "organization",
                table: "rooms",
                columns: new[] { "center_id", "archived_at_utc", "normalized_name" });

            migrationBuilder.CreateIndex(
                name: "ux_rooms_center_code",
                schema: "organization",
                table: "rooms",
                columns: new[] { "center_id", "normalized_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_rooms_center_name",
                schema: "organization",
                table: "rooms",
                columns: new[] { "center_id", "normalized_name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "rooms",
                schema: "organization");
        }
    }
}
