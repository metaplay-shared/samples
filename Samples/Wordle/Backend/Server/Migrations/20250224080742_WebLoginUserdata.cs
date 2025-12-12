using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Migrations
{
    /// <inheritdoc />
    public partial class WebLoginUserdata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ResolvedUserInfo",
                table: "WebLoginClientSessions",
                newName: "UserInfo");

            migrationBuilder.RenameColumn(
                name: "ResolvedUserInfo",
                table: "WebLoginAuthorizations",
                newName: "UserInfo");

            migrationBuilder.AddColumn<string>(
                name: "LoginMethod",
                table: "WebLoginClientSessions",
                type: "varchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "LoginMethod",
                table: "WebLoginAuthorizations",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LoginMethod",
                table: "WebLoginClientSessions");

            migrationBuilder.DropColumn(
                name: "LoginMethod",
                table: "WebLoginAuthorizations");

            migrationBuilder.RenameColumn(
                name: "UserInfo",
                table: "WebLoginClientSessions",
                newName: "ResolvedUserInfo");

            migrationBuilder.RenameColumn(
                name: "UserInfo",
                table: "WebLoginAuthorizations",
                newName: "ResolvedUserInfo");
        }
    }
}
