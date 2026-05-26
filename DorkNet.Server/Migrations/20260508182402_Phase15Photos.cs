using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DorkNet.Server.Migrations
{
    /// <inheritdoc />
    public partial class Phase15Photos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Cheers_FromPlayerId_TargetPlayerId_TargetRoomId_Type",
                table: "Cheers");

            migrationBuilder.AddColumn<long>(
                name: "TargetPhotoId",
                table: "Cheers",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "Photos",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UploaderPlayerId = table.Column<long>(type: "INTEGER", nullable: false),
                    BlobName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Caption = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    RoomId = table.Column<long>(type: "INTEGER", nullable: false),
                    TaggedPlayerIdsCsv = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    IsPublic = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CheerCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ViewCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Photos", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Cheers_FromPlayerId_TargetPlayerId_TargetRoomId_TargetPhotoId_Type",
                table: "Cheers",
                columns: new[] { "FromPlayerId", "TargetPlayerId", "TargetRoomId", "TargetPhotoId", "Type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Cheers_TargetPhotoId",
                table: "Cheers",
                column: "TargetPhotoId");

            migrationBuilder.CreateIndex(
                name: "IX_Photos_BlobName",
                table: "Photos",
                column: "BlobName");

            migrationBuilder.CreateIndex(
                name: "IX_Photos_CreatedAt",
                table: "Photos",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Photos_RoomId",
                table: "Photos",
                column: "RoomId");

            migrationBuilder.CreateIndex(
                name: "IX_Photos_UploaderPlayerId",
                table: "Photos",
                column: "UploaderPlayerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Photos");

            migrationBuilder.DropIndex(
                name: "IX_Cheers_FromPlayerId_TargetPlayerId_TargetRoomId_TargetPhotoId_Type",
                table: "Cheers");

            migrationBuilder.DropIndex(
                name: "IX_Cheers_TargetPhotoId",
                table: "Cheers");

            migrationBuilder.DropColumn(
                name: "TargetPhotoId",
                table: "Cheers");

            migrationBuilder.CreateIndex(
                name: "IX_Cheers_FromPlayerId_TargetPlayerId_TargetRoomId_Type",
                table: "Cheers",
                columns: new[] { "FromPlayerId", "TargetPlayerId", "TargetRoomId", "Type" },
                unique: true);
        }
    }
}
