using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DorkNet.Server.Migrations
{
    /// <inheritdoc />
    public partial class Phase21RoomScenes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RoomScenes",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RoomId = table.Column<long>(type: "INTEGER", nullable: false),
                    OrderIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false, collation: "NOCASE"),
                    RoomSceneLocationId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    DataBlobName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    MaxPlayers = table.Column<int>(type: "INTEGER", nullable: false),
                    IsSandbox = table.Column<bool>(type: "INTEGER", nullable: false),
                    CanMatchmakeInto = table.Column<bool>(type: "INTEGER", nullable: false),
                    DataModifiedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoomScenes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RoomScenes_RoomId_Name",
                table: "RoomScenes",
                columns: new[] { "RoomId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_RoomScenes_RoomId_OrderIndex",
                table: "RoomScenes",
                columns: new[] { "RoomId", "OrderIndex" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RoomScenes");
        }
    }
}
