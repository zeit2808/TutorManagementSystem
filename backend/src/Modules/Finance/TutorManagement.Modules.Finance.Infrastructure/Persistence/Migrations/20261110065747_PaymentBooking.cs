using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TutorManagement.Modules.Finance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PaymentBooking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "bank_transaction_id",
                schema: "finance",
                table: "receipts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source",
                schema: "finance",
                table: "receipts",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Manual");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "paid_at_utc",
                schema: "finance",
                table: "payment_intents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_receipts_bank_transaction_auto",
                schema: "finance",
                table: "receipts",
                column: "bank_transaction_id",
                unique: true,
                filter: "bank_transaction_id IS NOT NULL AND source = 'Auto'");

            migrationBuilder.CreateIndex(
                name: "ux_receipts_bank_transaction_live",
                schema: "finance",
                table: "receipts",
                column: "bank_transaction_id",
                unique: true,
                filter: "bank_transaction_id IS NOT NULL AND voided_at_utc IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_receipts_bank_transaction_auto",
                schema: "finance",
                table: "receipts");

            migrationBuilder.DropIndex(
                name: "ux_receipts_bank_transaction_live",
                schema: "finance",
                table: "receipts");

            migrationBuilder.DropColumn(
                name: "bank_transaction_id",
                schema: "finance",
                table: "receipts");

            migrationBuilder.DropColumn(
                name: "source",
                schema: "finance",
                table: "receipts");

            migrationBuilder.DropColumn(
                name: "paid_at_utc",
                schema: "finance",
                table: "payment_intents");
        }
    }
}
