using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Yukti.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRowLevelSecurity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // FR-DB-02 / FR-TENANT-01 Layer 2: database-level enforcement,
            // independent of the repository query filter (Layer 1) and
            // TenantGuard (Layer 3) — even a bug in either of those still
            // can't return another tenant's rows once these policies are
            // active. Keyed to current_setting('app.current_tenant_id'),
            // set once per request by Yukti.Api's middleware via
            // set_config(). The `true` second argument to current_setting
            // makes a missing/unset setting return NULL instead of
            // erroring, so any query run without the app-level middleware
            // (e.g. a raw psql session) sees zero rows rather than an
            // error — fail closed.
            // FORCE is required in addition to ENABLE: Postgres/CockroachDB
            // exempt the table OWNER from RLS by default, and our app
            // connects as the same role that owns these tables (it created
            // them via migrations) — without FORCE, every policy below
            // would be silently inert for our own application's queries,
            // the one connection that actually matters here.
            //
            // No FOR SELECT restriction / no separate WITH CHECK: the same
            // expression governs reads and writes symmetrically (the
            // simplest, most defensible RLS model) — a write that would
            // violate the policy is rejected at the database, not silently
            // filtered out afterward.
            migrationBuilder.Sql("ALTER TABLE flows ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE flows FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql(
                "CREATE POLICY tenant_isolation ON flows USING (\"TenantId\" = current_setting('app.current_tenant_id', true)::uuid);");

            migrationBuilder.Sql("ALTER TABLE flow_runs ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE flow_runs FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql(
                "CREATE POLICY tenant_isolation ON flow_runs USING (\"TenantId\" = current_setting('app.current_tenant_id', true)::uuid);");

            migrationBuilder.Sql("ALTER TABLE users ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE users FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql(
                "CREATE POLICY tenant_isolation ON users USING (\"TenantId\" = current_setting('app.current_tenant_id', true)::uuid);");

            // Baseline roles / built-in modules have TenantId IS NULL and
            // must stay visible to every tenant (and during process-startup
            // seeding, when app.current_tenant_id is unset entirely — the
            // "IS NULL" branch matches those global rows regardless).
            migrationBuilder.Sql("ALTER TABLE roles ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE roles FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql(
                "CREATE POLICY tenant_isolation ON roles USING (\"TenantId\" IS NULL OR \"TenantId\" = current_setting('app.current_tenant_id', true)::uuid);");

            migrationBuilder.Sql("ALTER TABLE module_registrations ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE module_registrations FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql(
                "CREATE POLICY tenant_isolation ON module_registrations USING (\"TenantId\" IS NULL OR \"TenantId\" = current_setting('app.current_tenant_id', true)::uuid);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON flows; ALTER TABLE flows NO FORCE ROW LEVEL SECURITY; ALTER TABLE flows DISABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON flow_runs; ALTER TABLE flow_runs NO FORCE ROW LEVEL SECURITY; ALTER TABLE flow_runs DISABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON users; ALTER TABLE users NO FORCE ROW LEVEL SECURITY; ALTER TABLE users DISABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON roles; ALTER TABLE roles NO FORCE ROW LEVEL SECURITY; ALTER TABLE roles DISABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON module_registrations; ALTER TABLE module_registrations NO FORCE ROW LEVEL SECURITY; ALTER TABLE module_registrations DISABLE ROW LEVEL SECURITY;");
        }
    }
}
