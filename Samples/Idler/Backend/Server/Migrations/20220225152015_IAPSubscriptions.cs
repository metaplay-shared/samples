using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Game.Server.Migrations
{
    public partial class IAPSubscriptions : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InAppPurchaseSubscriptions",
                columns: table => new
                {
                    PlayerAndOriginalTransactionId = table.Column<string>(type: "varchar(530)", maxLength: 530, nullable: false),
                    PlayerId = table.Column<string>(type: "varchar(64)", nullable: false),
                    OriginalTransactionId = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false),
                    SubscriptionInfo = table.Column<byte[]>(type: "longblob", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "DateTime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InAppPurchaseSubscriptions", x => x.PlayerAndOriginalTransactionId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InAppPurchaseSubscriptions_OriginalTransactionId",
                table: "InAppPurchaseSubscriptions",
                column: "OriginalTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_InAppPurchaseSubscriptions_PlayerId",
                table: "InAppPurchaseSubscriptions",
                column: "PlayerId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InAppPurchaseSubscriptions");
        }
    }
}
