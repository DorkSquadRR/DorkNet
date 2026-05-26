using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DorkNet.Server.Migrations
{
    /// <inheritdoc />
    public partial class Phase13EventsAndObjectives : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ObjectiveProgress",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PlayerId = table.Column<long>(type: "INTEGER", nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    IsCompleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    ClearedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ObjectiveProgress", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlayerEventResponses",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EventId = table.Column<long>(type: "INTEGER", nullable: false),
                    PlayerId = table.Column<long>(type: "INTEGER", nullable: false),
                    Response = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerEventResponses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlayerEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CreatorPlayerId = table.Column<long>(type: "INTEGER", nullable: false),
                    RoomId = table.Column<long>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    StartsAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndsAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Capacity = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ObjectiveProgress_PlayerId_Key",
                table: "ObjectiveProgress",
                columns: new[] { "PlayerId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerEventResponses_EventId_PlayerId",
                table: "PlayerEventResponses",
                columns: new[] { "EventId", "PlayerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerEventResponses_PlayerId",
                table: "PlayerEventResponses",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerEvents_CreatorPlayerId",
                table: "PlayerEvents",
                column: "CreatorPlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerEvents_StartsAt",
                table: "PlayerEvents",
                column: "StartsAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ObjectiveProgress");

            migrationBuilder.DropTable(
                name: "PlayerEventResponses");

            migrationBuilder.DropTable(
                name: "PlayerEvents");
        }
    }
}
