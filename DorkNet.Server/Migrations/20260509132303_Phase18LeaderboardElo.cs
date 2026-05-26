using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DorkNet.Server.Migrations
{
    /// <inheritdoc />
    public partial class Phase18LeaderboardElo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LeaderboardStats",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PlayerId = table.Column<long>(type: "INTEGER", nullable: false),
                    StatChannel = table.Column<int>(type: "INTEGER", nullable: false),
                    Value = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaderboardStats", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlayerElo",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PlayerId = table.Column<long>(type: "INTEGER", nullable: false),
                    GameMode = table.Column<int>(type: "INTEGER", nullable: false),
                    Elo = table.Column<int>(type: "INTEGER", nullable: false),
                    Wins = table.Column<int>(type: "INTEGER", nullable: false),
                    Losses = table.Column<int>(type: "INTEGER", nullable: false),
                    MatchesPlayed = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerElo", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LeaderboardStats_PlayerId_StatChannel",
                table: "LeaderboardStats",
                columns: new[] { "PlayerId", "StatChannel" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeaderboardStats_StatChannel_Value",
                table: "LeaderboardStats",
                columns: new[] { "StatChannel", "Value" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerElo_GameMode_Elo",
                table: "PlayerElo",
                columns: new[] { "GameMode", "Elo" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerElo_PlayerId_GameMode",
                table: "PlayerElo",
                columns: new[] { "PlayerId", "GameMode" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LeaderboardStats");

            migrationBuilder.DropTable(
                name: "PlayerElo");
        }
    }
}
