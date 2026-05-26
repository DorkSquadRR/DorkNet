using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DorkNet.Server.Migrations
{
    /// <inheritdoc />
    public partial class Phase9ReportsAndIpBans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IpBans",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Cidr = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    BannedByAdminId = table.Column<long>(type: "INTEGER", nullable: false),
                    BannedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Until = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IpBans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Reports",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ReporterPlayerId = table.Column<long>(type: "INTEGER", nullable: false),
                    TargetPlayerId = table.Column<long>(type: "INTEGER", nullable: false),
                    Category = table.Column<int>(type: "INTEGER", nullable: false),
                    Message = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    GameSessionId = table.Column<long>(type: "INTEGER", nullable: false),
                    RoomId = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ResolverAdminId = table.Column<long>(type: "INTEGER", nullable: true),
                    ResolutionNote = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reports", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IpBans_Cidr",
                table: "IpBans",
                column: "Cidr");

            migrationBuilder.CreateIndex(
                name: "IX_Reports_ReporterPlayerId",
                table: "Reports",
                column: "ReporterPlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_Reports_ResolvedAt",
                table: "Reports",
                column: "ResolvedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Reports_TargetPlayerId",
                table: "Reports",
                column: "TargetPlayerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IpBans");

            migrationBuilder.DropTable(
                name: "Reports");
        }
    }
}
