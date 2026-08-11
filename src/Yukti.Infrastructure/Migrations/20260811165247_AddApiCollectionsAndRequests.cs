using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Yukti.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddApiCollectionsAndRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "api_collections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_api_collections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "api_requests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Method = table.Column<string>(type: "text", nullable: false),
                    Url = table.Column<string>(type: "text", nullable: false),
                    headers = table.Column<string>(type: "jsonb", nullable: false),
                    query_params = table.Column<string>(type: "jsonb", nullable: false),
                    body = table.Column<string>(type: "jsonb", nullable: true),
                    assertions = table.Column<string>(type: "jsonb", nullable: true),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    ApiCollectionId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_api_requests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_api_requests_api_collections_ApiCollectionId",
                        column: x => x.ApiCollectionId,
                        principalTable: "api_collections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_api_collections_TenantId_Name",
                table: "api_collections",
                columns: new[] { "TenantId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_api_requests_ApiCollectionId_Order",
                table: "api_requests",
                columns: new[] { "ApiCollectionId", "Order" },
                unique: true);

            // FR-DB-02 / FR-TENANT-01 Layer 2, same pattern as AddRowLevelSecurity's
            // flows policy — api_requests needs no policy of its own, it's
            // only ever reached through its owning api_collections row
            // (same as flow_steps has none of its own).
            migrationBuilder.Sql("ALTER TABLE api_collections ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE api_collections FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql(
                "CREATE POLICY tenant_isolation ON api_collections USING (\"TenantId\" = current_setting('app.current_tenant_id', true)::uuid);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON api_collections; ALTER TABLE api_collections NO FORCE ROW LEVEL SECURITY; ALTER TABLE api_collections DISABLE ROW LEVEL SECURITY;");

            migrationBuilder.DropTable(
                name: "api_requests");

            migrationBuilder.DropTable(
                name: "api_collections");
        }
    }
}
