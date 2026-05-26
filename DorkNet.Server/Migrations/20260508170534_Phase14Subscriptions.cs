using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DorkNet.Server.Migrations
{
    /// <inheritdoc />
    public partial class Phase14Subscriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Subscriptions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SubscriberPlayerId = table.Column<long>(type: "INTEGER", nullable: false),
                    TargetPlayerId = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Subscriptions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_SubscriberPlayerId",
                table: "Subscriptions",
                column: "SubscriberPlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_SubscriberPlayerId_TargetPlayerId",
                table: "Subscriptions",
                columns: new[] { "SubscriberPlayerId", "TargetPlayerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_TargetPlayerId",
                table: "Subscriptions",
                column: "TargetPlayerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Subscriptions");
        }
    }
}
