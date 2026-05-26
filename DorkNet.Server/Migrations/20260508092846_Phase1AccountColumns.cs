using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DorkNet.Server.Migrations
{
    /// <inheritdoc />
    public partial class Phase1AccountColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "BannedUntil",
                table: "Players",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreatedFromDeviceId",
                table: "Players",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "Players",
                type: "TEXT",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsAdmin",
                table: "Players",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "LastIp",
                table: "Players",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Locale",
                table: "Players",
                type: "TEXT",
                maxLength: 16,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BannedUntil",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "CreatedFromDeviceId",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "IsAdmin",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "LastIp",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "Locale",
                table: "Players");
        }
    }
}
