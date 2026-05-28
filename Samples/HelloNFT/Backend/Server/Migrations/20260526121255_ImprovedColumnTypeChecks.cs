using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Migrations
{
    /// <inheritdoc />
    public partial class ImprovedColumnTypeChecks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "LoginMethod",
                table: "WebLoginAuthorizations",
                type: "varchar(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "FailureInfo",
                table: "StaticGameConfigs",
                type: "LONGTEXT",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.AlterColumn<ulong>(
                name: "UpdateCounter",
                table: "Nfts",
                type: "BIGINT UNSIGNED",
                nullable: false,
                oldClrType: typeof(ulong),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<int>(
                name: "Version",
                table: "MetaInfo",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "INTEGER")
                .OldAnnotation("Sqlite:Autoincrement", true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "LoginMethod",
                table: "WebLoginAuthorizations",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "FailureInfo",
                table: "StaticGameConfigs",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "LONGTEXT",
                oldNullable: true);

            migrationBuilder.AlterColumn<ulong>(
                name: "UpdateCounter",
                table: "Nfts",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(ulong),
                oldType: "BIGINT UNSIGNED");

            migrationBuilder.AlterColumn<int>(
                name: "Version",
                table: "MetaInfo",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "INTEGER")
                .Annotation("Sqlite:Autoincrement", true);
        }
    }
}
