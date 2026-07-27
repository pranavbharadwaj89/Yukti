using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Yukti.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddYuktiAppRuntimeRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // FR-AUDIT-03: closes the gap this session's earlier audit found
            // and documented (AuditEntryGrantsTests) — the app previously
            // connected as "pranav", the table owner, so REVOKE had no
            // effect. yukti_app is a genuinely separate, non-owner role:
            // migrations still run as pranav (owner privileges are
            // required for DDL/ALTER TABLE/RLS policy creation), while the
            // app's runtime connection (ConnectionStrings:YuktiRuntime)
            // uses this role instead.
            //
            // Password set to a placeholder here (safe to commit) and
            // rotated immediately after this migration applies, via
            // ALTER USER — the real password is never written to any
            // file that reaches source control, only to user-secrets.
            migrationBuilder.Sql("CREATE USER IF NOT EXISTS yukti_app WITH PASSWORD 'placeholder-rotate-immediately';");

            // Full CRUD on every table except audit_entries.
            string[] fullCrudTables =
            {
                "flows", "flow_steps", "flow_runs", "step_results", "retry_attempts",
                "module_registrations", "module_action_entries", "users", "roles",
                "idempotency_keys", "outbox_messages", "flow_reports", "trend_aggregates"
            };
            foreach (var table in fullCrudTables)
                migrationBuilder.Sql($"GRANT SELECT, INSERT, UPDATE, DELETE ON {table} TO yukti_app;");

            // FR-AUDIT-03's actual point: INSERT/SELECT only, no UPDATE/DELETE.
            migrationBuilder.Sql("GRANT SELECT, INSERT ON audit_entries TO yukti_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP USER IF EXISTS yukti_app;");
        }
    }
}
