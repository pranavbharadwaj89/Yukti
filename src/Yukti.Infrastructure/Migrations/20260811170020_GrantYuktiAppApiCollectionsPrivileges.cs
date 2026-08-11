using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Yukti.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class GrantYuktiAppApiCollectionsPrivileges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Same gap AddYuktiAppRuntimeRole closed for every table that
            // existed at the time — api_collections/api_requests didn't
            // exist yet, so yukti_app (the app's runtime connection role,
            // ConnectionStrings:YuktiRuntime) never got GRANTed on them.
            // Found live: every api-collections endpoint failed with
            // "user yukti_app does not have INSERT/SELECT privilege" until
            // this ran.
            migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE, DELETE ON api_collections TO yukti_app;");
            migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE, DELETE ON api_requests TO yukti_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("REVOKE SELECT, INSERT, UPDATE, DELETE ON api_requests FROM yukti_app;");
            migrationBuilder.Sql("REVOKE SELECT, INSERT, UPDATE, DELETE ON api_collections FROM yukti_app;");
        }
    }
}
