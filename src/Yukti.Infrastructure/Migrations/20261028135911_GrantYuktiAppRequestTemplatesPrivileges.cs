using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Yukti.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class GrantYuktiAppRequestTemplatesPrivileges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Same gap AddYuktiAppRuntimeRole closed for every table that existed at the time,
            // and GrantYuktiAppApiCollectionsPrivileges closed again for api_collections/
            // api_requests — request_templates didn't exist yet either, so yukti_app (the app's
            // runtime connection role, ConnectionStrings:YuktiRuntime) never got GRANTed on it.
            migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE, DELETE ON request_templates TO yukti_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("REVOKE SELECT, INSERT, UPDATE, DELETE ON request_templates FROM yukti_app;");
        }
    }
}
