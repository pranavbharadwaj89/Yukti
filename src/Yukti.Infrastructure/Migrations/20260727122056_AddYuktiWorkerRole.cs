using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Yukti.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddYuktiWorkerRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // CONFIRMED LIVE (2026-07-27): connecting as the table owner
            // ("pranav") with no tenant context returned rows across every
            // tenant despite every RLS policy in this database being
            // declared FORCE ROW LEVEL SECURITY — CockroachDB does not
            // enforce FORCE against the owner the way standard Postgres
            // does. Switching Yukti.Api to the genuinely non-owner
            // "yukti_app" role (AddYuktiAppRuntimeRole) fixed per-request
            // enforcement, but broke the background/batch jobs
            // (TrendAggregateBatchJob, OutboxRelayHostedService's
            // FlowReportProjectionConsumer) that legitimately need
            // cross-tenant visibility and have no per-request tenant
            // context to set. yukti_worker is a role dedicated to exactly
            // that: BYPASSRLS, used only by Yukti.Worker, never by
            // Yukti.Api's per-tenant HTTP request path.
            migrationBuilder.Sql("CREATE USER IF NOT EXISTS yukti_worker WITH PASSWORD 'placeholder-rotate-immediately' BYPASSRLS;");

            string[] fullCrudTables =
            {
                "flows", "flow_steps", "flow_runs", "step_results", "retry_attempts",
                "module_registrations", "module_action_entries", "users", "roles",
                "idempotency_keys", "outbox_messages", "flow_reports", "trend_aggregates"
            };
            foreach (var table in fullCrudTables)
                migrationBuilder.Sql($"GRANT SELECT, INSERT, UPDATE, DELETE ON {table} TO yukti_worker;");

            // Same restriction as yukti_app — a worker process has no more
            // business mutating a written audit trail than the API does.
            migrationBuilder.Sql("GRANT SELECT, INSERT ON audit_entries TO yukti_worker;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP USER IF EXISTS yukti_worker;");
        }
    }
}
