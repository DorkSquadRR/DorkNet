using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DorkNet.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddChatThreads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChatThreadMembers",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ThreadKey = table.Column<string>(type: "TEXT", nullable: false),
                    PlayerId = table.Column<long>(type: "INTEGER", nullable: false),
                    JoinedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SnoozeUntil = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastReadMessageId = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatThreadMembers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ChatThreads",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ThreadKey = table.Column<string>(type: "TEXT", maxLength: 96, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    CreatorPlayerId = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatThreads", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChatThreadMembers_PlayerId",
                table: "ChatThreadMembers",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatThreadMembers_ThreadKey_PlayerId",
                table: "ChatThreadMembers",
                columns: new[] { "ThreadKey", "PlayerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChatThreads_ThreadKey",
                table: "ChatThreads",
                column: "ThreadKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChatThreadMembers");

            migrationBuilder.DropTable(
                name: "ChatThreads");
        }
    }
}
