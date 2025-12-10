using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Game.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddDataProtector : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DataProtectorKeys",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(64)", nullable: false),
                    ServerName = table.Column<string>(type: "varchar(64)", nullable: false),
                    KeyBytes = table.Column<byte[]>(type: "longblob", nullable: false),
                    ValidFrom = table.Column<DateTime>(type: "DateTime", nullable: false),
                    ValidUntil = table.Column<DateTime>(type: "DateTime", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "DateTime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataProtectorKeys", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DataProtectorKeys_ExpiresAt",
                table: "DataProtectorKeys",
                column: "ExpiresAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DataProtectorKeys");
        }
    }
}
