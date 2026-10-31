using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TutorManagement.Modules.Finance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PaymentIntents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "payment_intents",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    center_id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(11)", maxLength: 11, nullable: false),
                    amount = table.Column<long>(type: "bigint", nullable: false),
                    charge_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    bank_bin = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                    account_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    account_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    cancelled_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_intents", x => x.id);
                    table.CheckConstraint("ck_payment_intents_amount", "amount > 0");
                });

            migrationBuilder.CreateIndex(
                name: "ix_payment_intents_pending",
                schema: "finance",
                table: "payment_intents",
                columns: new[] { "center_id", "student_id" },
                filter: "status = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "ux_payment_intents_code",
                schema: "finance",
                table: "payment_intents",
                column: "code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payment_intents",
                schema: "finance");
        }
    }
}
