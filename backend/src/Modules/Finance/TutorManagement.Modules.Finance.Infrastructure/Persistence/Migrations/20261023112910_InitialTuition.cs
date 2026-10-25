using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TutorManagement.Modules.Finance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialTuition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "finance");

            // H12: receipt numbers per center, handed out atomically (not an EF entity).
            migrationBuilder.Sql(
                "CREATE TABLE finance.receipt_counters (center_id uuid PRIMARY KEY, last_value integer NOT NULL CHECK (last_value > 0));");

            migrationBuilder.CreateTable(
                name: "class_billing_anchors",
                schema: "finance",
                columns: table => new
                {
                    center_id = table.Column<Guid>(type: "uuid", nullable: false),
                    class_id = table.Column<Guid>(type: "uuid", nullable: false),
                    anchor_date = table.Column<DateOnly>(type: "date", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_class_billing_anchors", x => new { x.center_id, x.class_id });
                });

            migrationBuilder.CreateTable(
                name: "class_fees",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    center_id = table.Column<Guid>(type: "uuid", nullable: false),
                    class_id = table.Column<Guid>(type: "uuid", nullable: false),
                    monthly_fee = table.Column<long>(type: "bigint", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_class_fees", x => x.id);
                    table.CheckConstraint("ck_class_fees_amount", "monthly_fee >= 0 AND monthly_fee % 1000 = 0");
                });

            migrationBuilder.CreateTable(
                name: "finance_settings",
                schema: "finance",
                columns: table => new
                {
                    center_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bank_bin = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: true),
                    bank_account_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    bank_account_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    due_days = table.Column<int>(type: "integer", nullable: false),
                    email_reminders = table.Column<bool>(type: "boolean", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_finance_settings", x => x.center_id);
                    table.CheckConstraint("ck_finance_settings_due_days", "due_days BETWEEN 0 AND 60");
                });

            migrationBuilder.CreateTable(
                name: "receipts",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    center_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    student_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payer_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    method = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    amount = table.Column<long>(type: "bigint", nullable: false),
                    received_on = table.Column<DateOnly>(type: "date", nullable: false),
                    note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    request_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    voided_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    voided_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    void_reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_receipts", x => x.id);
                    table.CheckConstraint("ck_receipts_amount", "amount > 0");
                });

            migrationBuilder.CreateTable(
                name: "tuition_charges",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    center_id = table.Column<Guid>(type: "uuid", nullable: false),
                    class_id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_id = table.Column<Guid>(type: "uuid", nullable: false),
                    enrollment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cycle_index = table.Column<int>(type: "integer", nullable: false),
                    period_start = table.Column<DateOnly>(type: "date", nullable: false),
                    period_end = table.Column<DateOnly>(type: "date", nullable: false),
                    monthly_fee = table.Column<long>(type: "bigint", nullable: false),
                    join_sessions = table.Column<int>(type: "integer", nullable: true),
                    period_sessions = table.Column<int>(type: "integer", nullable: true),
                    list_amount = table.Column<long>(type: "bigint", nullable: false),
                    discount_amount = table.Column<long>(type: "bigint", nullable: false),
                    amount = table.Column<long>(type: "bigint", nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ended_on = table.Column<DateOnly>(type: "date", nullable: true),
                    ended_sessions_attended = table.Column<int>(type: "integer", nullable: true),
                    ended_sessions_total = table.Column<int>(type: "integer", nullable: true),
                    ended_value = table.Column<long>(type: "bigint", nullable: true),
                    waived_amount = table.Column<long>(type: "bigint", nullable: false),
                    reminded_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    adjustment_amount = table.Column<long>(type: "bigint", nullable: false),
                    adjustment_sessions = table.Column<int>(type: "integer", nullable: true),
                    adjustment_reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    adjusted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    adjusted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tuition_charges", x => x.id);
                    table.CheckConstraint("ck_tuition_charges_amounts", "amount >= 0 AND list_amount >= 0 AND discount_amount >= 0 AND adjustment_amount >= 0 AND waived_amount >= 0");
                    table.CheckConstraint("ck_tuition_charges_period", "period_end >= period_start");
                });

            migrationBuilder.CreateTable(
                name: "tuition_discounts",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    center_id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_id = table.Column<Guid>(type: "uuid", nullable: false),
                    class_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    value = table.Column<long>(type: "bigint", nullable: false),
                    from_date = table.Column<DateOnly>(type: "date", nullable: false),
                    to_date = table.Column<DateOnly>(type: "date", nullable: true),
                    reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tuition_discounts", x => x.id);
                    table.CheckConstraint("ck_tuition_discounts_value", "(kind = 'Percent' AND value BETWEEN 1 AND 100) OR (kind = 'Amount' AND value > 0 AND value % 1000 = 0)");
                });

            migrationBuilder.CreateTable(
                name: "receipt_allocations",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    receipt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    charge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<long>(type: "bigint", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_receipt_allocations", x => x.id);
                    table.CheckConstraint("ck_receipt_allocations_amount", "amount > 0");
                    table.ForeignKey(
                        name: "fk_receipt_allocations_charge",
                        column: x => x.charge_id,
                        principalSchema: "finance",
                        principalTable: "tuition_charges",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_receipt_allocations_receipt",
                        column: x => x.receipt_id,
                        principalSchema: "finance",
                        principalTable: "receipts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_class_fees_class",
                schema: "finance",
                table: "class_fees",
                columns: new[] { "center_id", "class_id", "effective_from" });

            migrationBuilder.CreateIndex(
                name: "ix_receipt_allocations_charge",
                schema: "finance",
                table: "receipt_allocations",
                column: "charge_id");

            migrationBuilder.CreateIndex(
                name: "ux_receipt_allocations_line",
                schema: "finance",
                table: "receipt_allocations",
                columns: new[] { "receipt_id", "charge_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_receipts_student",
                schema: "finance",
                table: "receipts",
                columns: new[] { "center_id", "student_id" });

            migrationBuilder.CreateIndex(
                name: "ux_receipts_number",
                schema: "finance",
                table: "receipts",
                columns: new[] { "center_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_receipts_request",
                schema: "finance",
                table: "receipts",
                columns: new[] { "center_id", "request_id" },
                unique: true,
                filter: "request_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_tuition_charges_class",
                schema: "finance",
                table: "tuition_charges",
                columns: new[] { "center_id", "class_id", "period_start" });

            migrationBuilder.CreateIndex(
                name: "ix_tuition_charges_student",
                schema: "finance",
                table: "tuition_charges",
                columns: new[] { "center_id", "student_id", "period_start" });

            migrationBuilder.CreateIndex(
                name: "ux_tuition_charges_enrollment_cycle",
                schema: "finance",
                table: "tuition_charges",
                columns: new[] { "enrollment_id", "cycle_index" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tuition_discounts_student",
                schema: "finance",
                table: "tuition_discounts",
                columns: new[] { "center_id", "student_id", "class_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS finance.receipt_counters;");

            migrationBuilder.DropTable(
                name: "class_billing_anchors",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "class_fees",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "finance_settings",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "receipt_allocations",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "tuition_discounts",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "tuition_charges",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "receipts",
                schema: "finance");
        }
    }
}
