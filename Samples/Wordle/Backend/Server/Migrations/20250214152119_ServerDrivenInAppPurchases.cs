using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Migrations
{
    /// <inheritdoc />
    public partial class ServerDrivenInAppPurchases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ServerDrivenInAppPurchases",
                columns: table => new
                {
                    TransactionId = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false),
                    PlayerId = table.Column<string>(type: "varchar(64)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "DateTime", nullable: false),
                    PurchasePlatform = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    PurchasePlatformUserId = table.Column<string>(type: "varchar(512)", nullable: false),
                    ProductId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServerDrivenInAppPurchases", x => x.TransactionId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ServerDrivenInAppPurchases_PlayerId",
                table: "ServerDrivenInAppPurchases",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_ServerDrivenInAppPurchases_PurchasePlatform_PurchasePlatformUserId_ProductId",
                table: "ServerDrivenInAppPurchases",
                columns: new[] { "PurchasePlatform", "PurchasePlatformUserId", "ProductId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ServerDrivenInAppPurchases");
        }
    }
}
