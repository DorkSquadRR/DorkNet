using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DorkNet.Server.Migrations
{
    /// <inheritdoc />
    public partial class Phase8AccountPassword : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "Birthday",
                table: "Players",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PasswordHash",
                table: "Players",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Phone",
                table: "Players",
                type: "TEXT",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Birthday",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "PasswordHash",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "Phone",
                table: "Players");
        }
    }
}
