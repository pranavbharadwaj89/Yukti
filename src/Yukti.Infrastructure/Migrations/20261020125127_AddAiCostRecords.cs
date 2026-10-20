using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Yukti.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAiCostRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ai_cost_records",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Capability = table.Column<string>(type: "text", nullable: false),
                    Provider = table.Column<string>(type: "text", nullable: false),
                    PromptVersion = table.Column<string>(type: "text", nullable: false),
                    Attempt = table.Column<int>(type: "integer", nullable: false),
                    Succeeded = table.Column<bool>(type: "boolean", nullable: false),
                    BudgetRefused = table.Column<bool>(type: "boolean", nullable: false),
                    InputTokens = table.Column<int>(type: "integer", nullable: false),
                    OutputTokens = table.Column<int>(type: "integer", nullable: false),
                    CostUsd = table.Column<decimal>(type: "numeric(12,6)", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_cost_records", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ai_cost_records_TenantId_OccurredAt",
                table: "ai_cost_records",
                columns: new[] { "TenantId", "OccurredAt" });

            // Tenant data like flow_reports (CLAUDE.md §9/§7: never weaken or skip RLS for tenant
            // tables) — a tenant's AI spend is itself sensitive, same reasoning as audit entries.
            migrationBuilder.Sql("ALTER TABLE ai_cost_records ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE ai_cost_records FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql(
                "CREATE POLICY tenant_isolation ON ai_cost_records USING (\"TenantId\" = current_setting('app.current_tenant_id', true)::uuid);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON ai_cost_records; ALTER TABLE ai_cost_records NO FORCE ROW LEVEL SECURITY; ALTER TABLE ai_cost_records DISABLE ROW LEVEL SECURITY;");

            migrationBuilder.DropTable(
                name: "ai_cost_records");
        }
    }
}
