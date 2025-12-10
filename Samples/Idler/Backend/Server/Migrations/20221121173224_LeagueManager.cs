using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Game.Server.Migrations
{
    public partial class LeagueManager : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LeagueManagers",
                columns: table => new
                {
                    EntityId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    PersistedAt = table.Column<DateTime>(type: "DateTime", nullable: false),
                    Payload = table.Column<byte[]>(type: "longblob", nullable: true),
                    SchemaVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    IsFinal = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeagueManagers", x => x.EntityId);
                });

            migrationBuilder.CreateTable(
                name: "LeagueParticipantDivisionAssociations",
                columns: table => new
                {
                    ParticipantId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    LeagueId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    DivisionId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "DateTime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeagueParticipantDivisionAssociations", x => x.ParticipantId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LeagueParticipantDivisionAssociations_DivisionId",
                table: "LeagueParticipantDivisionAssociations",
                column: "DivisionId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LeagueManagers");

            migrationBuilder.DropTable(
                name: "LeagueParticipantDivisionAssociations");
        }
    }
}
