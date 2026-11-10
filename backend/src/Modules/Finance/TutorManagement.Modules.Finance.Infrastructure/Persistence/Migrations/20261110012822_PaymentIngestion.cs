using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TutorManagement.Modules.Finance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PaymentIngestion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "payment_audit_events",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    center_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    target_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: true),
                    before_json = table.Column<string>(type: "jsonb", nullable: true),
                    after_json = table.Column<string>(type: "jsonb", nullable: true),
                    at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_audit_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "payment_provider_configs",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    center_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    endpoint_key = table.Column<string>(type: "character varying(43)", maxLength: 43, nullable: false),
                    webhook_secret = table.Column<byte[]>(type: "bytea", nullable: false),
                    api_token = table.Column<byte[]>(type: "bytea", nullable: false),
                    api_token_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    key_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    bank_bin = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                    account_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    mode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    auto_since_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    pull_requested_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    next_pull_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    pull_retry_count = table.Column<int>(type: "integer", nullable: false),
                    last_pulled_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    pull_cursor = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    pull_processing_id = table.Column<Guid>(type: "uuid", nullable: true),
                    pull_processing_until_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_pull_error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    invalid_signature_count = table.Column<long>(type: "bigint", nullable: false),
                    last_invalid_signature_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_provider_configs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "bank_transactions",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    config_id = table.Column<Guid>(type: "uuid", nullable: false),
                    center_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    provider_txn_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    bank_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    bank_bin = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: true),
                    account_number = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    provider_bank_account_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    virtual_account = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    reference_code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    transfer_type = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    amount = table.Column<long>(type: "bigint", nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    transacted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    raw_payload = table.Column<string>(type: "jsonb", nullable: false),
                    semantic_key = table.Column<string>(type: "character varying(258)", maxLength: 258, nullable: true),
                    duplicate_of_id = table.Column<Guid>(type: "uuid", nullable: true),
                    payload_checksum = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    fetched_by = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    first_seen_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    seen_count = table.Column<int>(type: "integer", nullable: false),
                    booked_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    review_reason = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    ignore_reason = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    intent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    student_id = table.Column<Guid>(type: "uuid", nullable: true),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    processing_id = table.Column<Guid>(type: "uuid", nullable: true),
                    processing_until_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    shadow_outcome = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    shadow_reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    shadow_intent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    shadow_student_id = table.Column<Guid>(type: "uuid", nullable: true),
                    shadow_evaluated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bank_transactions", x => x.id);
                    table.CheckConstraint("ck_bank_transactions_amount", "amount > 0");
                    table.ForeignKey(
                        name: "FK_bank_transactions_bank_transactions_duplicate_of_id",
                        column: x => x.duplicate_of_id,
                        principalSchema: "finance",
                        principalTable: "bank_transactions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_bank_transactions_payment_provider_configs_config_id",
                        column: x => x.config_id,
                        principalSchema: "finance",
                        principalTable: "payment_provider_configs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payment_webhook_deliveries",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    config_id = table.Column<Guid>(type: "uuid", nullable: false),
                    webhook_id = table.Column<long>(type: "bigint", nullable: false),
                    reference_code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    amount = table.Column<long>(type: "bigint", nullable: true),
                    raw_body = table.Column<string>(type: "text", nullable: false),
                    delivery_count = table.Column<int>(type: "integer", nullable: false),
                    first_seen_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_webhook_deliveries", x => x.id);
                    table.ForeignKey(
                        name: "FK_payment_webhook_deliveries_payment_provider_configs_config_~",
                        column: x => x.config_id,
                        principalSchema: "finance",
                        principalTable: "payment_provider_configs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bank_transactions_center_amount",
                schema: "finance",
                table: "bank_transactions",
                columns: new[] { "center_id", "amount" });

            migrationBuilder.CreateIndex(
                name: "ix_bank_transactions_center_date",
                schema: "finance",
                table: "bank_transactions",
                columns: new[] { "center_id", "transacted_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_bank_transactions_center_status",
                schema: "finance",
                table: "bank_transactions",
                columns: new[] { "center_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_bank_transactions_config_id",
                schema: "finance",
                table: "bank_transactions",
                column: "config_id");

            migrationBuilder.CreateIndex(
                name: "IX_bank_transactions_duplicate_of_id",
                schema: "finance",
                table: "bank_transactions",
                column: "duplicate_of_id");

            migrationBuilder.CreateIndex(
                name: "ix_bank_transactions_pending",
                schema: "finance",
                table: "bank_transactions",
                column: "next_attempt_at_utc",
                filter: "status = 'Received'");

            migrationBuilder.CreateIndex(
                name: "ux_bank_transactions_provider_txn",
                schema: "finance",
                table: "bank_transactions",
                columns: new[] { "provider", "provider_txn_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_bank_transactions_semantic_key",
                schema: "finance",
                table: "bank_transactions",
                column: "semantic_key",
                unique: true,
                filter: "semantic_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_payment_audit_events_center",
                schema: "finance",
                table: "payment_audit_events",
                columns: new[] { "center_id", "at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_payment_provider_configs_due",
                schema: "finance",
                table: "payment_provider_configs",
                column: "next_pull_at_utc",
                filter: "mode <> 'Off'");

            migrationBuilder.CreateIndex(
                name: "ux_payment_provider_configs_auto_account",
                schema: "finance",
                table: "payment_provider_configs",
                columns: new[] { "bank_bin", "account_number" },
                unique: true,
                filter: "mode = 'Auto'");

            migrationBuilder.CreateIndex(
                name: "ux_payment_provider_configs_center",
                schema: "finance",
                table: "payment_provider_configs",
                columns: new[] { "center_id", "provider" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_payment_provider_configs_endpoint",
                schema: "finance",
                table: "payment_provider_configs",
                column: "endpoint_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_payment_provider_configs_token",
                schema: "finance",
                table: "payment_provider_configs",
                columns: new[] { "provider", "api_token_fingerprint" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_payment_webhook_deliveries_webhook",
                schema: "finance",
                table: "payment_webhook_deliveries",
                columns: new[] { "config_id", "webhook_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bank_transactions",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "payment_audit_events",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "payment_webhook_deliveries",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "payment_provider_configs",
                schema: "finance");
        }
    }
}
