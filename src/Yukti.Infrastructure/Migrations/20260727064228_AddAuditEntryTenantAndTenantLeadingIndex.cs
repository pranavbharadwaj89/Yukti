using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Yukti.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditEntryTenantAndTenantLeadingIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_flows_FamilyId_Version",
                table: "flows");

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "audit_entries",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_flows_TenantId_FamilyId_Version",
                table: "flows",
                columns: new[] { "TenantId", "FamilyId", "Version" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_flows_TenantId_FamilyId_Version",
                table: "flows");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "audit_entries");

            migrationBuilder.CreateIndex(
                name: "IX_flows_FamilyId_Version",
                table: "flows",
                columns: new[] { "FamilyId", "Version" });
        }
    }
}
