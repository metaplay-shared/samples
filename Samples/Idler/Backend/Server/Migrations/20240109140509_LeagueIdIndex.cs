using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Game.Server.Migrations
{
    /// <inheritdoc />
    public partial class LeagueIdIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_LeagueParticipantDivisionAssociations_LeagueId",
                table: "LeagueParticipantDivisionAssociations",
                column: "LeagueId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LeagueParticipantDivisionAssociations_LeagueId",
                table: "LeagueParticipantDivisionAssociations");
        }
    }
}
