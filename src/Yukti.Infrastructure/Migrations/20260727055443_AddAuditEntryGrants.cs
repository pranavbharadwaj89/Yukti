using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Yukti.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditEntryGrants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // FR-AUDIT-03: the application DB role gets INSERT/SELECT only
            // on audit_entries — no UPDATE/DELETE grant exists, so a compromised
            // or buggy app connection cannot tamper with or remove an audit
            // trail it already wrote. REVOKE FROM PUBLIC first so no other
            // role picks up default table-owner-adjacent privileges either.
            //
            // CONFIRMED GAP (same category as this repo's other documented
            // environment gaps — Redis-backed rate limiting, real OTLP
            // collector), verified live via AuditEntryGrantsTests: this
            // REVOKE has no practical effect, because the app connects as
            // "pranav", the same role that owns audit_entries (it created
            // the table via migrations) — Postgres/CockroachDB table
            // owners retain implicit DML privileges independent of
            // GRANT/REVOKE. Closing this for real requires migrations to
            // run as a separate owner/admin role while the app's runtime
            // connection string uses a distinct, lower-privileged role
            // that only ever receives this grant — a live infrastructure
            // change (new role + credential + updated connection string),
            // not something this migration alone can accomplish.
            migrationBuilder.Sql("REVOKE UPDATE, DELETE ON audit_entries FROM public;");
            migrationBuilder.Sql("REVOKE UPDATE, DELETE ON audit_entries FROM pranav;");
            migrationBuilder.Sql("GRANT SELECT, INSERT ON audit_entries TO pranav;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("GRANT UPDATE, DELETE ON audit_entries TO pranav;");
        }
    }
}
