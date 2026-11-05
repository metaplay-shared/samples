using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Migrations
{
    /// <inheritdoc />
    public partial class ProductIdForPersistedIAPSubscription : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InAppPurchaseSubscriptions_OriginalTransactionId",
                table: "InAppPurchaseSubscriptions");

            migrationBuilder.AlterColumn<string>(
                name: "PlayerAndOriginalTransactionId",
                table: "InAppPurchaseSubscriptions",
                type: "varchar(659)",
                maxLength: 659,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(530)",
                oldMaxLength: 530);

            migrationBuilder.AddColumn<string>(
                name: "ProductId",
                table: "InAppPurchaseSubscriptions",
                type: "varchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_InAppPurchaseSubscriptions_OriginalTransactionId_ProductId",
                table: "InAppPurchaseSubscriptions",
                columns: new[] { "OriginalTransactionId", "ProductId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InAppPurchaseSubscriptions_OriginalTransactionId_ProductId",
                table: "InAppPurchaseSubscriptions");

            migrationBuilder.DropColumn(
                name: "ProductId",
                table: "InAppPurchaseSubscriptions");

            migrationBuilder.AlterColumn<string>(
                name: "PlayerAndOriginalTransactionId",
                table: "InAppPurchaseSubscriptions",
                type: "varchar(530)",
                maxLength: 530,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(659)",
                oldMaxLength: 659);

            migrationBuilder.CreateIndex(
                name: "IX_InAppPurchaseSubscriptions_OriginalTransactionId",
                table: "InAppPurchaseSubscriptions",
                column: "OriginalTransactionId");
        }
    }
}
