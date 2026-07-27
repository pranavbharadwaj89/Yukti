using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Yukti.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditEntryAndIdempotencyRowLevelSecurity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // FR-DB-02: closes the gap found during this session's FR-DB
            // audit — audit_entries and idempotency_keys were the two
            // per-tenant operational tables the original AddRowLevelSecurity
            // migration predates (audit_entries didn't exist yet;
            // idempotency_keys was added by FR-API-02 afterward, and RLS
            // was never revisited for it). Same FORCE-required, fail-closed
            // pattern as every other RLS policy in this codebase.
            //
            // audit_entries.TenantId is nullable (self-registration,
            // startup seeding — see AuditEntry's doc comment), so it uses
            // the same "IS NULL OR ..." permissive branch as roles/
            // module_registrations, not the strict flows/flow_runs/users form.
            migrationBuilder.Sql("ALTER TABLE audit_entries ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE audit_entries FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql(
                "CREATE POLICY tenant_isolation ON audit_entries USING (\"TenantId\" IS NULL OR \"TenantId\" = current_setting('app.current_tenant_id', true)::uuid);");

            migrationBuilder.Sql("ALTER TABLE idempotency_keys ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE idempotency_keys FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql(
                "CREATE POLICY tenant_isolation ON idempotency_keys USING (\"TenantId\" = current_setting('app.current_tenant_id', true)::uuid);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON audit_entries; ALTER TABLE audit_entries NO FORCE ROW LEVEL SECURITY; ALTER TABLE audit_entries DISABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON idempotency_keys; ALTER TABLE idempotency_keys NO FORCE ROW LEVEL SECURITY; ALTER TABLE idempotency_keys DISABLE ROW LEVEL SECURITY;");
        }
    }
}
