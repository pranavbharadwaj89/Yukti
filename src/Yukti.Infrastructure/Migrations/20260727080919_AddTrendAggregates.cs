using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Yukti.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTrendAggregates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "trend_aggregates",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    TotalRunsLast24h = table.Column<int>(type: "integer", nullable: false),
                    PassRateLast24h = table.Column<double>(type: "double precision", nullable: false),
                    FlakeRateLast24h = table.Column<double>(type: "double precision", nullable: false),
                    LastUpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trend_aggregates", x => x.TenantId);
                });

            // FR-DB-02: TenantId is this table's own PK, not just a
            // column, but RLS still matters — without it any connection
            // could SELECT every tenant's row, PK or not.
            migrationBuilder.Sql("ALTER TABLE trend_aggregates ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE trend_aggregates FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql(
                "CREATE POLICY tenant_isolation ON trend_aggregates USING (\"TenantId\" = current_setting('app.current_tenant_id', true)::uuid);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON trend_aggregates; ALTER TABLE trend_aggregates NO FORCE ROW LEVEL SECURITY; ALTER TABLE trend_aggregates DISABLE ROW LEVEL SECURITY;");

            migrationBuilder.DropTable(
                name: "trend_aggregates");
        }
    }
}
