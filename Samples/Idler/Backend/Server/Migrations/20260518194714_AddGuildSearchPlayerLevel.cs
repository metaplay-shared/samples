using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Game.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddGuildSearchPlayerLevel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RequiredPlayerLevel",
                table: "Guilds",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Guilds_EntityId_RequiredPlayerLevel",
                table: "Guilds",
                columns: new[] { "EntityId", "RequiredPlayerLevel" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Guilds_EntityId_RequiredPlayerLevel",
                table: "Guilds");

            migrationBuilder.DropColumn(
                name: "RequiredPlayerLevel",
                table: "Guilds");
        }
    }
}
