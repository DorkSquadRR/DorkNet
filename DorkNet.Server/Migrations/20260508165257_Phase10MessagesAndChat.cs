using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DorkNet.Server.Migrations
{
    /// <inheritdoc />
    public partial class Phase10MessagesAndChat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChatMessages",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ThreadKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SenderPlayerId = table.Column<long>(type: "INTEGER", nullable: false),
                    Body = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    SentAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Messages",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SenderPlayerId = table.Column<long>(type: "INTEGER", nullable: false),
                    RecipientPlayerId = table.Column<long>(type: "INTEGER", nullable: false),
                    Body = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    SentAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ReadAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Messages", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_ThreadKey_SentAt",
                table: "ChatMessages",
                columns: new[] { "ThreadKey", "SentAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Messages_RecipientPlayerId_SentAt",
                table: "Messages",
                columns: new[] { "RecipientPlayerId", "SentAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Messages_SenderPlayerId_SentAt",
                table: "Messages",
                columns: new[] { "SenderPlayerId", "SentAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChatMessages");

            migrationBuilder.DropTable(
                name: "Messages");
        }
    }
}
