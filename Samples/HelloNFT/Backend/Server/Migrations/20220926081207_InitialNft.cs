using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Migrations
{
    public partial class InitialNft : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NftManagers",
                columns: table => new
                {
                    EntityId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    PersistedAt = table.Column<DateTime>(type: "DateTime", nullable: false),
                    Payload = table.Column<byte[]>(type: "longblob", nullable: false),
                    SchemaVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    IsFinal = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NftManagers", x => x.EntityId);
                });

            migrationBuilder.CreateTable(
                name: "Nfts",
                columns: table => new
                {
                    GlobalId = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false),
                    CollectionId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    TokenId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    OwnerEntityId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    OwnerAddress = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true),
                    PersistedAt = table.Column<DateTime>(type: "DateTime", nullable: false),
                    Payload = table.Column<byte[]>(type: "longblob", nullable: false),
                    UpdateCounter = table.Column<ulong>(type: "INTEGER", nullable: false),
                    SchemaVersion = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Nfts", x => x.GlobalId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Nfts_CollectionId",
                table: "Nfts",
                column: "CollectionId");

            migrationBuilder.CreateIndex(
                name: "IX_Nfts_OwnerAddress",
                table: "Nfts",
                column: "OwnerAddress");

            migrationBuilder.CreateIndex(
                name: "IX_Nfts_OwnerEntityId",
                table: "Nfts",
                column: "OwnerEntityId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NftManagers");

            migrationBuilder.DropTable(
                name: "Nfts");
        }
    }
}
