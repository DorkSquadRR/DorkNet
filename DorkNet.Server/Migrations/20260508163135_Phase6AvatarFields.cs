using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DorkNet.Server.Migrations
{
    /// <inheritdoc />
    public partial class Phase6AvatarFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FaceFeatures",
                table: "Avatars",
                type: "TEXT",
                maxLength: 8192,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "HairColor",
                table: "Avatars",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "OutfitSelections",
                table: "Avatars",
                type: "TEXT",
                maxLength: 2048,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SavedOutfitsJson",
                table: "Avatars",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SkinColor",
                table: "Avatars",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "Avatars",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FaceFeatures",
                table: "Avatars");

            migrationBuilder.DropColumn(
                name: "HairColor",
                table: "Avatars");

            migrationBuilder.DropColumn(
                name: "OutfitSelections",
                table: "Avatars");

            migrationBuilder.DropColumn(
                name: "SavedOutfitsJson",
                table: "Avatars");

            migrationBuilder.DropColumn(
                name: "SkinColor",
                table: "Avatars");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Avatars");
        }
    }
}
