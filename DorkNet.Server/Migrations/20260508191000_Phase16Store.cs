using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DorkNet.Server.Migrations
{
    /// <inheritdoc />
    public partial class Phase16Store : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StoreItems",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Slug = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ImageName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    CurrencyType = table.Column<int>(type: "INTEGER", nullable: false),
                    Price = table.Column<long>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsLimitedTime = table.Column<bool>(type: "INTEGER", nullable: false),
                    AvailableUntil = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Storefront = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreItems", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StoreItems_IsActive_Category",
                table: "StoreItems",
                columns: new[] { "IsActive", "Category" });

            migrationBuilder.CreateIndex(
                name: "IX_StoreItems_Slug",
                table: "StoreItems",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StoreItems_Storefront",
                table: "StoreItems",
                column: "Storefront");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StoreItems");
        }
    }
}
