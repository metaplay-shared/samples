using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Migrations
{
    /// <inheritdoc />
    public partial class PlayerGuildSearchPriority : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<uint>(
                name: "Priority",
                table: "PlayerNameSearches",
                type: "tinyint",
                nullable: false,
                defaultValue: 64u);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerNameSearches_Priority_NamePart_EntityId",
                table: "PlayerNameSearches",
                columns: new[] { "Priority", "NamePart", "EntityId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PlayerNameSearches_Priority_NamePart_EntityId",
                table: "PlayerNameSearches");

            migrationBuilder.DropColumn(
                name: "Priority",
                table: "PlayerNameSearches");
        }
    }
}
