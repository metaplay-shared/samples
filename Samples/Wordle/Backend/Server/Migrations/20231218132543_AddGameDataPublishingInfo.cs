using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Migrations
{
    /// <inheritdoc />
    public partial class AddGameDataPublishingInfo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "PublishedAt",
                table: "StaticGameConfigs",
                type: "DateTime",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UnpublishedAt",
                table: "StaticGameConfigs",
                type: "DateTime",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PublishedAt",
                table: "StaticGameConfigs");

            migrationBuilder.DropColumn(
                name: "UnpublishedAt",
                table: "StaticGameConfigs");
        }
    }
}
