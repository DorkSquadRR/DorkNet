using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DorkNet.Server.Migrations
{
    /// <inheritdoc />
    public partial class Phase19TokensIgnoresCards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Cards",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PlayerId = table.Column<long>(type: "INTEGER", nullable: true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Body = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    ImageName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ActionUrl = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cards", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlatformIgnores",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PlayerId = table.Column<long>(type: "INTEGER", nullable: false),
                    Platform = table.Column<int>(type: "INTEGER", nullable: false),
                    PlatformUserId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformIgnores", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PushTokens",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PlayerId = table.Column<long>(type: "INTEGER", nullable: false),
                    Platform = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Token = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PushTokens", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Cards_ExpiresAt",
                table: "Cards",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_Cards_PlayerId",
                table: "Cards",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformIgnores_PlayerId_Platform_PlatformUserId",
                table: "PlatformIgnores",
                columns: new[] { "PlayerId", "Platform", "PlatformUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PushTokens_PlayerId_Platform",
                table: "PushTokens",
                columns: new[] { "PlayerId", "Platform" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PushTokens_Token",
                table: "PushTokens",
                column: "Token");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Cards");

            migrationBuilder.DropTable(
                name: "PlatformIgnores");

            migrationBuilder.DropTable(
                name: "PushTokens");
        }
    }
}
