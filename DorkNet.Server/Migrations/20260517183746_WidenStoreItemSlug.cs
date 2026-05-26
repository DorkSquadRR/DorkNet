using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DorkNet.Server.Migrations
{
    /// <inheritdoc />
    public partial class WidenStoreItemSlug : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LoadingScreenTips",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Message = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    ImageName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Context = table.Column<int>(type: "INTEGER", nullable: false),
                    PlatformMask = table.Column<int>(type: "INTEGER", nullable: false),
                    RoomNamesCsv = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoadingScreenTips", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LoadingScreenTips");
        }
    }
}
