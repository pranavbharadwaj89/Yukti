using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Yukti.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOutboxAndFlowReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "flow_reports",
                columns: table => new
                {
                    FlowRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    FlowId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    FinalStatus = table.Column<string>(type: "text", nullable: false),
                    PassedCount = table.Column<int>(type: "integer", nullable: false),
                    FailedCount = table.Column<int>(type: "integer", nullable: false),
                    SkippedCount = table.Column<int>(type: "integer", nullable: false),
                    TotalDuration = table.Column<TimeSpan>(type: "interval", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProjectedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_flow_reports", x => x.FlowRunId);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventTypeName = table.Column<string>(type: "text", nullable: false),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_messages", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_flow_reports_TenantId",
                table: "flow_reports",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_ProcessedAt",
                table: "outbox_messages",
                column: "ProcessedAt");

            // FR-DB-02: flow_reports carries TenantId (strict, never null —
            // every FlowRunCompletedEvent traces back to a real tenant's
            // FlowRun) so it gets the same RLS policy as flows/flow_runs.
            // outbox_messages deliberately does NOT: it has no TenantId
            // column of its own (events of many tenants interleave in one
            // queue, exactly like a real message broker's topic would),
            // and it is never read by tenant-scoped application code —
            // only OutboxRelayHostedService touches it, using the app's
            // own (non-tenant-scoped) connection. Treated as internal
            // infrastructure, like __EFMigrationsHistory, not tenant data.
            migrationBuilder.Sql("ALTER TABLE flow_reports ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE flow_reports FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql(
                "CREATE POLICY tenant_isolation ON flow_reports USING (\"TenantId\" = current_setting('app.current_tenant_id', true)::uuid);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON flow_reports; ALTER TABLE flow_reports NO FORCE ROW LEVEL SECURITY; ALTER TABLE flow_reports DISABLE ROW LEVEL SECURITY;");

            migrationBuilder.DropTable(
                name: "flow_reports");

            migrationBuilder.DropTable(
                name: "outbox_messages");
        }
    }
}
