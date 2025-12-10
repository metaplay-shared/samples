using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Game.Server.Migrations
{
    /// <inheritdoc />
    public partial class WebLoginFlows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WebLoginAuthorizations",
                columns: table => new
                {
                    AuthorizationCode = table.Column<string>(type: "char(32)", nullable: false),
                    ClientId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    RedirectUri = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false),
                    S256CodeChallenge = table.Column<byte[]>(type: "binary(32)", nullable: true),
                    State = table.Column<string>(type: "varchar(2048)", maxLength: 2048, nullable: true),
                    Scope = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    FlowExpiresAt = table.Column<DateTime>(type: "DateTime", nullable: false),
                    CodeExchangeExpiresAt = table.Column<DateTime>(type: "DateTime", nullable: false),
                    Phase = table.Column<int>(type: "int", nullable: false),
                    ResolvedUserInfo = table.Column<byte[]>(type: "longblob", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebLoginAuthorizations", x => x.AuthorizationCode);
                });

            migrationBuilder.CreateTable(
                name: "WebLoginClientSessions",
                columns: table => new
                {
                    ClientSessionId = table.Column<string>(type: "char(32)", nullable: false),
                    RefreshTokenNonce = table.Column<string>(type: "char(32)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "DateTime", nullable: false),
                    ClientId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    Scope = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "DateTime", nullable: false),
                    ResolvedUserInfo = table.Column<byte[]>(type: "longblob", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebLoginClientSessions", x => x.ClientSessionId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WebLoginAuthorizations");

            migrationBuilder.DropTable(
                name: "WebLoginClientSessions");
        }
    }
}
