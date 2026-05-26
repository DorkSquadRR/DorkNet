using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DorkNet.Server.Migrations
{
    /// <inheritdoc />
    public partial class Phase11Cheers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Cheers",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FromPlayerId = table.Column<long>(type: "INTEGER", nullable: false),
                    TargetPlayerId = table.Column<long>(type: "INTEGER", nullable: false),
                    TargetRoomId = table.Column<long>(type: "INTEGER", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    CheeredAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cheers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Cheers_FromPlayerId_TargetPlayerId_TargetRoomId_Type",
                table: "Cheers",
                columns: new[] { "FromPlayerId", "TargetPlayerId", "TargetRoomId", "Type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Cheers_TargetPlayerId",
                table: "Cheers",
                column: "TargetPlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_Cheers_TargetRoomId",
                table: "Cheers",
                column: "TargetRoomId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Cheers");
        }
    }
}
