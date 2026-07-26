using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Yukti.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "flow_runs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FlowId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Trigger = table.Column<string>(type: "text", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FinishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    variables = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_flow_runs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "flows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    ContinueOnFailure = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    declared_variables = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_flows", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "module_registrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    DisplayName = table.Column<string>(type: "text", nullable: false),
                    Trust = table.Column<string>(type: "text", nullable: false),
                    ContractVersion = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_module_registrations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "roles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    permissions = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_roles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "text", nullable: false),
                    DisplayName = table.Column<string>(type: "text", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsServiceAccount = table.Column<bool>(type: "boolean", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: false),
                    role_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "step_results",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceStepId = table.Column<Guid>(type: "uuid", nullable: false),
                    StepName = table.Column<string>(type: "text", nullable: false),
                    Module = table.Column<string>(type: "text", nullable: false),
                    Action = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Duration = table.Column<TimeSpan>(type: "interval", nullable: false),
                    Message = table.Column<string>(type: "text", nullable: true),
                    Error = table.Column<string>(type: "text", nullable: true),
                    data = table.Column<string>(type: "jsonb", nullable: true),
                    ai_attribution = table.Column<string>(type: "jsonb", nullable: true),
                    FlowRunId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_step_results", x => x.Id);
                    table.ForeignKey(
                        name: "FK_step_results_flow_runs_FlowRunId",
                        column: x => x.FlowRunId,
                        principalTable: "flow_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "flow_steps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Module = table.Column<string>(type: "text", nullable: false),
                    Action = table.Column<string>(type: "text", nullable: false),
                    @params = table.Column<string>(name: "params", type: "jsonb", nullable: false),
                    SaveAs = table.Column<string>(type: "text", nullable: true),
                    when_condition = table.Column<string>(type: "text", nullable: true),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    FlowId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_flow_steps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_flow_steps_flows_FlowId",
                        column: x => x.FlowId,
                        principalTable: "flows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "module_action_entries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActionName = table.Column<string>(type: "text", nullable: false),
                    schema = table.Column<string>(type: "jsonb", nullable: false),
                    IsDeprecated = table.Column<bool>(type: "boolean", nullable: false),
                    DeprecationNotice = table.Column<string>(type: "text", nullable: true),
                    ModuleRegistrationId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_module_action_entries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_module_action_entries_module_registrations_ModuleRegistrati~",
                        column: x => x.ModuleRegistrationId,
                        principalTable: "module_registrations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "retry_attempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AttemptNumber = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Duration = table.Column<TimeSpan>(type: "interval", nullable: false),
                    Error = table.Column<string>(type: "text", nullable: true),
                    AttemptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StepResultId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_retry_attempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_retry_attempts_step_results_StepResultId",
                        column: x => x.StepResultId,
                        principalTable: "step_results",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_flow_steps_FlowId_Order",
                table: "flow_steps",
                columns: new[] { "FlowId", "Order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_flows_FamilyId_Version",
                table: "flows",
                columns: new[] { "FamilyId", "Version" });

            migrationBuilder.CreateIndex(
                name: "IX_module_action_entries_ModuleRegistrationId",
                table: "module_action_entries",
                column: "ModuleRegistrationId");

            migrationBuilder.CreateIndex(
                name: "IX_retry_attempts_StepResultId",
                table: "retry_attempts",
                column: "StepResultId");

            migrationBuilder.CreateIndex(
                name: "IX_step_results_FlowRunId",
                table: "step_results",
                column: "FlowRunId");

            migrationBuilder.CreateIndex(
                name: "IX_users_Email",
                table: "users",
                column: "Email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "flow_steps");

            migrationBuilder.DropTable(
                name: "module_action_entries");

            migrationBuilder.DropTable(
                name: "retry_attempts");

            migrationBuilder.DropTable(
                name: "roles");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "flows");

            migrationBuilder.DropTable(
                name: "module_registrations");

            migrationBuilder.DropTable(
                name: "step_results");

            migrationBuilder.DropTable(
                name: "flow_runs");
        }
    }
}
