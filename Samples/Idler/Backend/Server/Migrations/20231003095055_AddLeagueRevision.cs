using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Game.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddLeagueRevision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LeagueStateRevision",
                table: "LeagueParticipantDivisionAssociations",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_LeagueParticipantDivisionAssociations_LeagueStateRevision",
                table: "LeagueParticipantDivisionAssociations",
                column: "LeagueStateRevision");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LeagueParticipantDivisionAssociations_LeagueStateRevision",
                table: "LeagueParticipantDivisionAssociations");

            migrationBuilder.DropColumn(
                name: "LeagueStateRevision",
                table: "LeagueParticipantDivisionAssociations");
        }
    }
}
