using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Game.Server.Migrations
{
    /// <inheritdoc />
    public partial class ChangeLeagueAssignmentKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_LeagueParticipantDivisionAssociations",
                table: "LeagueParticipantDivisionAssociations");

            migrationBuilder.DropIndex(
                name: "IX_LeagueParticipantDivisionAssociations_LeagueId",
                table: "LeagueParticipantDivisionAssociations");

            migrationBuilder.AddPrimaryKey(
                name: "PK_LeagueParticipantDivisionAssociations",
                table: "LeagueParticipantDivisionAssociations",
                columns: new[] { "LeagueId", "ParticipantId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_LeagueParticipantDivisionAssociations",
                table: "LeagueParticipantDivisionAssociations");

            migrationBuilder.AddPrimaryKey(
                name: "PK_LeagueParticipantDivisionAssociations",
                table: "LeagueParticipantDivisionAssociations",
                column: "ParticipantId");

            migrationBuilder.CreateIndex(
                name: "IX_LeagueParticipantDivisionAssociations_LeagueId",
                table: "LeagueParticipantDivisionAssociations",
                column: "LeagueId");
        }
    }
}
