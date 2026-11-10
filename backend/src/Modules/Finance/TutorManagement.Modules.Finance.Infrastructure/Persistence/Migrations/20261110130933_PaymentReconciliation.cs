using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TutorManagement.Modules.Finance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PaymentReconciliation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "digest_sent_on",
                schema: "finance",
                table: "payment_provider_configs",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "next_reconcile_at_utc",
                schema: "finance",
                table: "payment_provider_configs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "reconcile_processing_id",
                schema: "finance",
                table: "payment_provider_configs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "reconcile_processing_until_utc",
                schema: "finance",
                table: "payment_provider_configs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "reconcile_requested_at_utc",
                schema: "finance",
                table: "payment_provider_configs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "payment_alerts",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    center_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fingerprint = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    detail = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    metric = table.Column<long>(type: "bigint", nullable: true),
                    first_seen_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_emailed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolved_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    acknowledged_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    acknowledged_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_alerts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "payment_reconciliation_findings",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    center_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    bank_transaction_id = table.Column<Guid>(type: "uuid", nullable: true),
                    receipt_id = table.Column<Guid>(type: "uuid", nullable: true),
                    delivery_id = table.Column<Guid>(type: "uuid", nullable: true),
                    amount = table.Column<long>(type: "bigint", nullable: true),
                    detail = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_reconciliation_findings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "payment_reconciliation_runs",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    center_id = table.Column<Guid>(type: "uuid", nullable: false),
                    config_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trigger = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    window_from_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    window_to_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    scanned_count = table.Column<int>(type: "integer", nullable: false),
                    missed_count = table.Column<int>(type: "integer", nullable: false),
                    changed_count = table.Column<int>(type: "integer", nullable: false),
                    incoming_total = table.Column<long>(type: "bigint", nullable: false),
                    booked_total = table.Column<long>(type: "bigint", nullable: false),
                    booked_receipt_total = table.Column<long>(type: "bigint", nullable: false),
                    pending_total = table.Column<long>(type: "bigint", nullable: false),
                    failed_total = table.Column<long>(type: "bigint", nullable: false),
                    ignored_total = table.Column<long>(type: "bigint", nullable: false),
                    finding_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_reconciliation_runs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_payment_audit_events_action",
                schema: "finance",
                table: "payment_audit_events",
                columns: new[] { "action", "at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_payment_alerts_state",
                schema: "finance",
                table: "payment_alerts",
                columns: new[] { "center_id", "state" });

            migrationBuilder.CreateIndex(
                name: "ux_payment_alerts_fingerprint",
                schema: "finance",
                table: "payment_alerts",
                columns: new[] { "center_id", "fingerprint" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payment_reconciliation_findings_run",
                schema: "finance",
                table: "payment_reconciliation_findings",
                column: "run_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_reconciliation_runs_center",
                schema: "finance",
                table: "payment_reconciliation_runs",
                columns: new[] { "center_id", "started_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payment_alerts",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "payment_reconciliation_findings",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "payment_reconciliation_runs",
                schema: "finance");

            migrationBuilder.DropIndex(
                name: "ix_payment_audit_events_action",
                schema: "finance",
                table: "payment_audit_events");

            migrationBuilder.DropColumn(
                name: "digest_sent_on",
                schema: "finance",
                table: "payment_provider_configs");

            migrationBuilder.DropColumn(
                name: "next_reconcile_at_utc",
                schema: "finance",
                table: "payment_provider_configs");

            migrationBuilder.DropColumn(
                name: "reconcile_processing_id",
                schema: "finance",
                table: "payment_provider_configs");

            migrationBuilder.DropColumn(
                name: "reconcile_processing_until_utc",
                schema: "finance",
                table: "payment_provider_configs");

            migrationBuilder.DropColumn(
                name: "reconcile_requested_at_utc",
                schema: "finance",
                table: "payment_provider_configs");
        }
    }
}
