using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Yukti.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStepResultsTenantIdAndPartitionPrepIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Nullable first, backfilled from the owning flow_runs row, then
            // locked to NOT NULL — never defaulted to a placeholder GUID:
            // step_results already has real FlowRunId→flow_runs.TenantId
            // provenance for every existing row, so there is no "unknown
            // tenant" case a default would paper over.
            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "step_results",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE step_results SET \"TenantId\" = fr.\"TenantId\" " +
                "FROM flow_runs fr WHERE fr.\"Id\" = step_results.\"FlowRunId\";");

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "step_results",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_step_results_TenantId",
                table: "step_results",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_flow_runs_TenantId_StartedAt",
                table: "flow_runs",
                columns: new[] { "TenantId", "StartedAt" });

            // FR-OPS-04 fallout: step_results previously had no RLS policy
            // at all — FlowRun's own RLS and the repository-level tenant
            // filter (Layer 1/2) protected list/read access through
            // IFlowRunRepository, but nothing at the database level stopped
            // a direct query against step_results itself from crossing
            // tenants. Same pattern as flow_runs' own policy.
            migrationBuilder.Sql(
                "ALTER TABLE step_results ENABLE ROW LEVEL SECURITY, FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql(
                "CREATE POLICY tenant_isolation ON step_results AS PERMISSIVE FOR ALL TO public " +
                "USING (\"TenantId\" = current_setting('app.current_tenant_id', true)::UUID);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON step_results;");
            migrationBuilder.Sql("ALTER TABLE step_results DISABLE ROW LEVEL SECURITY;");

            migrationBuilder.DropIndex(
                name: "IX_step_results_TenantId",
                table: "step_results");

            migrationBuilder.DropIndex(
                name: "IX_flow_runs_TenantId_StartedAt",
                table: "flow_runs");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "step_results");
        }
    }
}
