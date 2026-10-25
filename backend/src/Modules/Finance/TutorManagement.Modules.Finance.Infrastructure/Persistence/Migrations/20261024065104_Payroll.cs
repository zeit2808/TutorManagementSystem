using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TutorManagement.Modules.Finance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Payroll : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // C11: payout numbers per center, handed out atomically (not an EF entity).
            migrationBuilder.Sql(
                "CREATE TABLE finance.payout_counters (center_id uuid PRIMARY KEY, last_value integer NOT NULL CHECK (last_value > 0));");

            migrationBuilder.CreateTable(
                name: "pay_rates",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    teacher_id = table.Column<Guid>(type: "uuid", nullable: false),
                    class_id = table.Column<Guid>(type: "uuid", nullable: true),
                    amount = table.Column<long>(type: "bigint", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    center_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    voided_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    voided_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    void_reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pay_rates", x => x.id);
                    table.CheckConstraint("ck_pay_rates_amount", "amount >= 0 AND amount % 1000 = 0");
                });

            migrationBuilder.CreateTable(
                name: "payroll_adjustments",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    month = table.Column<DateOnly>(type: "date", nullable: false),
                    teacher_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<long>(type: "bigint", nullable: false),
                    reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    center_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    voided_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    voided_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    void_reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payroll_adjustments", x => x.id);
                    table.CheckConstraint("ck_payroll_adjustments_amount", "amount <> 0 AND amount % 1000 = 0");
                });

            migrationBuilder.CreateTable(
                name: "payroll_carries",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    center_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_month = table.Column<DateOnly>(type: "date", nullable: false),
                    into_month = table.Column<DateOnly>(type: "date", nullable: false),
                    teacher_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payroll_carries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "payroll_config_changes",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    center_id = table.Column<Guid>(type: "uuid", nullable: false),
                    previous_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    previous_mode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    mode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payroll_config_changes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "payroll_configs",
                schema: "finance",
                columns: table => new
                {
                    center_id = table.Column<Guid>(type: "uuid", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    mode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payroll_configs", x => x.center_id);
                });

            migrationBuilder.CreateTable(
                name: "payroll_disputes",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    center_id = table.Column<Guid>(type: "uuid", nullable: false),
                    month = table.Column<DateOnly>(type: "date", nullable: false),
                    teacher_id = table.Column<Guid>(type: "uuid", nullable: false),
                    message = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resolved_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    resolution = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payroll_disputes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "payroll_month_events",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    center_id = table.Column<Guid>(type: "uuid", nullable: false),
                    month = table.Column<DateOnly>(type: "date", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payroll_month_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "payroll_months",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    center_id = table.Column<Guid>(type: "uuid", nullable: false),
                    month = table.Column<DateOnly>(type: "date", nullable: false),
                    mode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    closed_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    closed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payroll_months", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "payroll_payouts",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    month = table.Column<DateOnly>(type: "date", nullable: false),
                    teacher_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<long>(type: "bigint", nullable: false),
                    method = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    paid_on = table.Column<DateOnly>(type: "date", nullable: false),
                    note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    request_id = table.Column<Guid>(type: "uuid", nullable: true),
                    center_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    voided_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    voided_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    void_reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payroll_payouts", x => x.id);
                    table.CheckConstraint("ck_payroll_payouts_amount", "amount > 0");
                });

            migrationBuilder.CreateTable(
                name: "shared_expenses",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    month = table.Column<DateOnly>(type: "date", nullable: false),
                    spent_on = table.Column<DateOnly>(type: "date", nullable: false),
                    category = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    amount = table.Column<long>(type: "bigint", nullable: false),
                    note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    center_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    voided_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    voided_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    void_reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shared_expenses", x => x.id);
                    table.CheckConstraint("ck_shared_expenses_amount", "amount > 0");
                });

            migrationBuilder.CreateTable(
                name: "payroll_month_lines",
                schema: "finance",
                columns: table => new
                {
                    teacher_id = table.Column<Guid>(type: "uuid", nullable: false),
                    month_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sessions = table.Column<int>(type: "integer", nullable: false),
                    minutes = table.Column<int>(type: "integer", nullable: false),
                    base_amount = table.Column<long>(type: "bigint", nullable: false),
                    adjustments = table.Column<long>(type: "bigint", nullable: false),
                    carried = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payroll_month_lines", x => new { x.month_id, x.teacher_id });
                    table.ForeignKey(
                        name: "FK_payroll_month_lines_payroll_months_month_id",
                        column: x => x.month_id,
                        principalSchema: "finance",
                        principalTable: "payroll_months",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_pay_rates_teacher",
                schema: "finance",
                table: "pay_rates",
                columns: new[] { "center_id", "teacher_id" });

            migrationBuilder.CreateIndex(
                name: "ix_payroll_adjustments_month",
                schema: "finance",
                table: "payroll_adjustments",
                columns: new[] { "center_id", "month" });

            migrationBuilder.CreateIndex(
                name: "ix_payroll_carries_into",
                schema: "finance",
                table: "payroll_carries",
                columns: new[] { "center_id", "into_month" });

            migrationBuilder.CreateIndex(
                name: "ix_payroll_config_changes_center",
                schema: "finance",
                table: "payroll_config_changes",
                columns: new[] { "center_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_payroll_disputes_month",
                schema: "finance",
                table: "payroll_disputes",
                columns: new[] { "center_id", "month" });

            migrationBuilder.CreateIndex(
                name: "ix_payroll_month_events_month",
                schema: "finance",
                table: "payroll_month_events",
                columns: new[] { "center_id", "month" });

            migrationBuilder.CreateIndex(
                name: "ux_payroll_months_month",
                schema: "finance",
                table: "payroll_months",
                columns: new[] { "center_id", "month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payroll_payouts_month",
                schema: "finance",
                table: "payroll_payouts",
                columns: new[] { "center_id", "month" });

            migrationBuilder.CreateIndex(
                name: "ux_payroll_payouts_number",
                schema: "finance",
                table: "payroll_payouts",
                columns: new[] { "center_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_payroll_payouts_request",
                schema: "finance",
                table: "payroll_payouts",
                columns: new[] { "center_id", "request_id" },
                unique: true,
                filter: "request_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_shared_expenses_month",
                schema: "finance",
                table: "shared_expenses",
                columns: new[] { "center_id", "month" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS finance.payout_counters;");

            migrationBuilder.DropTable(
                name: "pay_rates",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "payroll_adjustments",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "payroll_carries",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "payroll_config_changes",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "payroll_configs",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "payroll_disputes",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "payroll_month_events",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "payroll_month_lines",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "payroll_payouts",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "shared_expenses",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "payroll_months",
                schema: "finance");
        }
    }
}
