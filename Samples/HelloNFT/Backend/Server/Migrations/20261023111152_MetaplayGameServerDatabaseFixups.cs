using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Migrations
{
    /// <inheritdoc />
    public partial class MetaplayGameServerDatabaseFixups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MetaplayGameServerDatabaseFixups",
                columns: table => new
                {
                    FixupsKey = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    FixupVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "DateTime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MetaplayGameServerDatabaseFixups", x => x.FixupsKey);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MetaplayGameServerDatabaseFixups");
        }
    }
}
