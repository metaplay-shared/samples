using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Game.Server.Migrations
{
    /// <inheritdoc />
    public partial class PlayerDeletionColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DeletionSource",
                table: "Players",
                type: "tinyint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LifecycleStatus",
                table: "Players",
                type: "tinyint",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ScheduledForDeletionAt",
                table: "Players",
                type: "DateTime",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeletionSource",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "LifecycleStatus",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "ScheduledForDeletionAt",
                table: "Players");
        }
    }
}
