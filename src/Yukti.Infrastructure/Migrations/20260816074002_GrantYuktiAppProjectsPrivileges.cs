using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Yukti.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class GrantYuktiAppProjectsPrivileges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Same gap GrantYuktiAppApiCollectionsPrivileges closed for
            // api_collections/api_requests — projects/test_environments
            // didn't exist when yukti_app's grants were set up, so it has
            // none on them by default.
            migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE, DELETE ON projects TO yukti_app;");
            migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE, DELETE ON test_environments TO yukti_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("REVOKE SELECT, INSERT, UPDATE, DELETE ON test_environments FROM yukti_app;");
            migrationBuilder.Sql("REVOKE SELECT, INSERT, UPDATE, DELETE ON projects FROM yukti_app;");
        }
    }
}
