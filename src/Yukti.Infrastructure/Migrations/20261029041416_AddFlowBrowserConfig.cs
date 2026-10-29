using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Yukti.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFlowBrowserConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "browser_config",
                table: "flows",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "browser_config",
                table: "flows");
        }
    }
}
