using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Migrations
{
    /// <inheritdoc />
    public partial class WebLoginFlowExpirationIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_WebLoginClientSessions_ExpiresAt",
                table: "WebLoginClientSessions",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_WebLoginAuthorizations_FlowExpiresAt",
                table: "WebLoginAuthorizations",
                column: "FlowExpiresAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WebLoginClientSessions_ExpiresAt",
                table: "WebLoginClientSessions");

            migrationBuilder.DropIndex(
                name: "IX_WebLoginAuthorizations_FlowExpiresAt",
                table: "WebLoginAuthorizations");
        }
    }
}
