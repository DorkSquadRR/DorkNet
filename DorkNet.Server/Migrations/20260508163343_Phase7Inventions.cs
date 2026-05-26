using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DorkNet.Server.Migrations
{
    /// <inheritdoc />
    public partial class Phase7Inventions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Inventions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CreatorPlayerId = table.Column<long>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    ImageName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Permission = table.Column<int>(type: "INTEGER", nullable: false),
                    CurrentBlobName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    TagsCsv = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    CheerCount = table.Column<int>(type: "INTEGER", nullable: false),
                    SpawnCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Inventions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Inventions_CheerCount",
                table: "Inventions",
                column: "CheerCount");

            migrationBuilder.CreateIndex(
                name: "IX_Inventions_CreatorPlayerId",
                table: "Inventions",
                column: "CreatorPlayerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Inventions");
        }
    }
}
