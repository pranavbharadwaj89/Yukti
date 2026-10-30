using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Yukti.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddObjectRepository : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "object_repositories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    pages = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_object_repositories", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_object_repositories_TenantId_Name",
                table: "object_repositories",
                columns: new[] { "TenantId", "Name" });

            // WEB-T2's own checklist explicitly calls for a raw-SQL-level tenant isolation test
            // "as in Day 6's T1" -- same real RLS policy AddRowLevelSecurity/
            // AddApiCollectionsAndRequests already applies, not just the app-level TenantId
            // filter EfObjectRepositoryRepository also has. (RequestTemplate's own migration
            // skipped this -- flagged separately, not copied forward here.)
            migrationBuilder.Sql("ALTER TABLE object_repositories ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE object_repositories FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql(
                "CREATE POLICY tenant_isolation ON object_repositories USING (\"TenantId\" = current_setting('app.current_tenant_id', true)::uuid);");

            migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE, DELETE ON object_repositories TO yukti_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("REVOKE SELECT, INSERT, UPDATE, DELETE ON object_repositories FROM yukti_app;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON object_repositories; ALTER TABLE object_repositories NO FORCE ROW LEVEL SECURITY; ALTER TABLE object_repositories DISABLE ROW LEVEL SECURITY;");

            migrationBuilder.DropTable(
                name: "object_repositories");
        }
    }
}
