using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DorkNet.Server.Migrations
{
    /// <inheritdoc />
    public partial class Phase3RoomDataBlobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CurrentDataBlobName",
                table: "Rooms",
                type: "TEXT",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "RoomDataBlobs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RoomId = table.Column<long>(type: "INTEGER", nullable: false),
                    BlobName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    UploadedByPlayerId = table.Column<long>(type: "INTEGER", nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Bytes = table.Column<byte[]>(type: "BLOB", nullable: false),
                    ReferencedFilenamesCsv = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoomDataBlobs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RoomDataBlobs_BlobName",
                table: "RoomDataBlobs",
                column: "BlobName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RoomDataBlobs_RoomId",
                table: "RoomDataBlobs",
                column: "RoomId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RoomDataBlobs");

            migrationBuilder.DropColumn(
                name: "CurrentDataBlobName",
                table: "Rooms");
        }
    }
}
