using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Game.Server.Migrations
{
    /// <inheritdoc />
    public partial class StatisticsEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StatisticsEvents",
                columns: table => new
                {
                    UniqueKey = table.Column<string>(type: "varchar(128)", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "DateTime", nullable: false),
                    Payload = table.Column<byte[]>(type: "longblob", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StatisticsEvents", x => x.UniqueKey);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StatisticsEvents_Timestamp",
                table: "StatisticsEvents",
                column: "Timestamp");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StatisticsEvents");
        }
    }
}
