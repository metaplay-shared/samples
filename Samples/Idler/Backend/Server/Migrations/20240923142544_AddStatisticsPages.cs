using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Game.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddStatisticsPages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StatisticsPages",
                columns: table => new
                {
                    UniqueKey = table.Column<string>(type: "varchar(128)", nullable: false),
                    ResolutionName = table.Column<string>(type: "varchar(128)", nullable: false),
                    StartTime = table.Column<DateTime>(type: "DateTime", nullable: false),
                    EndTime = table.Column<DateTime>(type: "DateTime", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "DateTime", nullable: false),
                    Payload = table.Column<byte[]>(type: "longblob", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StatisticsPages", x => x.UniqueKey);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StatisticsPages_CreatedAt",
                table: "StatisticsPages",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_StatisticsPages_EndTime",
                table: "StatisticsPages",
                column: "EndTime");

            migrationBuilder.CreateIndex(
                name: "IX_StatisticsPages_StartTime",
                table: "StatisticsPages",
                column: "StartTime");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StatisticsPages");
        }
    }
}
