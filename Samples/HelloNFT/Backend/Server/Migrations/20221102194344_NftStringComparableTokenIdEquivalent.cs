using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Migrations
{
    public partial class NftStringComparableTokenIdEquivalent : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "StringComparableTokenIdEquivalent",
                table: "Nfts",
                type: "varchar(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_Nfts_StringComparableTokenIdEquivalent",
                table: "Nfts",
                column: "StringComparableTokenIdEquivalent");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Nfts_StringComparableTokenIdEquivalent",
                table: "Nfts");

            migrationBuilder.DropColumn(
                name: "StringComparableTokenIdEquivalent",
                table: "Nfts");
        }
    }
}
