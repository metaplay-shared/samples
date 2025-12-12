using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Migrations
{
    /// <inheritdoc />
    public partial class FixTimestampPrecision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateTime>(
                name: "Timestamp",
                table: "StatisticsEvents",
                type: "DateTime(3)",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "DateTime");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateTime>(
                name: "Timestamp",
                table: "StatisticsEvents",
                type: "DateTime",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "DateTime(3)");
        }
    }
}
