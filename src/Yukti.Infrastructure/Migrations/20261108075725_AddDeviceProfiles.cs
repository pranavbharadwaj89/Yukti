using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Yukti.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "device_profiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Platform = table.Column<string>(type: "text", nullable: false),
                    PlatformVersion = table.Column<string>(type: "text", nullable: true),
                    DeviceName = table.Column<string>(type: "text", nullable: true),
                    App = table.Column<string>(type: "text", nullable: true),
                    AppiumUrl = table.Column<string>(type: "text", nullable: false),
                    EndpointKind = table.Column<string>(type: "text", nullable: false),
                    FarmCredentialRef = table.Column<string>(type: "text", nullable: true),
                    Reset = table.Column<string>(type: "text", nullable: false),
                    SessionTimeout = table.Column<TimeSpan>(type: "interval", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    extra_capabilities = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device_profiles", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_device_profiles_TenantId_Name",
                table: "device_profiles",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            // MOB-T2's own checklist explicitly calls for a raw-SQL-level tenant isolation test
            // "as in Day 6's T1" -- the same real RLS policy AddRowLevelSecurity/
            // AddObjectRepository already apply, not just the app-level TenantId filter
            // EfDeviceProfileRepository also has. (RequestTemplate's own migration skipped this --
            // flagged there, not copied forward here; this table follows object_repositories'
            // corrected pattern instead.)
            migrationBuilder.Sql("ALTER TABLE device_profiles ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE device_profiles FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql(
                "CREATE POLICY tenant_isolation ON device_profiles USING (\"TenantId\" = current_setting('app.current_tenant_id', true)::uuid);");

            migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE, DELETE ON device_profiles TO yukti_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("REVOKE SELECT, INSERT, UPDATE, DELETE ON device_profiles FROM yukti_app;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON device_profiles; ALTER TABLE device_profiles NO FORCE ROW LEVEL SECURITY; ALTER TABLE device_profiles DISABLE ROW LEVEL SECURITY;");

            migrationBuilder.DropTable(
                name: "device_profiles");
        }
    }
}
