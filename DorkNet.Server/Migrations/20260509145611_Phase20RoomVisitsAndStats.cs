using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DorkNet.Server.Migrations
{
    /// <inheritdoc />
    public partial class Phase20RoomVisitsAndStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "VisitorCount",
                table: "Rooms",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "RoomVisits",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RoomId = table.Column<long>(type: "INTEGER", nullable: false),
                    PlayerId = table.Column<long>(type: "INTEGER", nullable: false),
                    FirstVisitAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastVisitAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    VisitCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoomVisits", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RoomVisits_PlayerId",
                table: "RoomVisits",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_RoomVisits_RoomId_LastVisitAt",
                table: "RoomVisits",
                columns: new[] { "RoomId", "LastVisitAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RoomVisits_RoomId_PlayerId",
                table: "RoomVisits",
                columns: new[] { "RoomId", "PlayerId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RoomVisits");

            migrationBuilder.DropColumn(
                name: "VisitorCount",
                table: "Rooms");
        }
    }
}
