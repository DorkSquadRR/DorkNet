using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DorkNet.Server.Migrations
{
    /// <summary>
    /// Columns needed by the March-2023 client wire fixes: the playlist
    /// settings the client can set (accessibility, level voting, VR/junior
    /// restrictions, content warning) which used to be acknowledged and thrown
    /// away, plus a host-chosen room code on private instances.
    ///
    /// Hand-trimmed after generation: `migrations add` diffs against the
    /// committed model snapshot, which already lagged the entity model, so the
    /// generated file also carried unrelated pre-existing drift
    /// (ServerSettings, Rooms, Avatars, ...). Replaying that against a database
    /// where those columns already exist fails on the first duplicate
    /// ADD COLUMN, so only the columns this change introduces are kept.
    /// </summary>
    public partial class Client2023WireFixes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RoomCode",
                table: "PrivateInstances",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Accessibility",
                table: "Playlists",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "CustomWarning",
                table: "Playlists",
                type: "TEXT",
                maxLength: 512,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "SupportsJuniors",
                table: "Playlists",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "SupportsLevelVoting",
                table: "Playlists",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "SupportsScreens",
                table: "Playlists",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "SupportsTeleportVR",
                table: "Playlists",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "SupportsWalkVR",
                table: "Playlists",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "WarningMask",
                table: "Playlists",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RoomCode",
                table: "PrivateInstances");

            migrationBuilder.DropColumn(
                name: "Accessibility",
                table: "Playlists");

            migrationBuilder.DropColumn(
                name: "CustomWarning",
                table: "Playlists");

            migrationBuilder.DropColumn(
                name: "SupportsJuniors",
                table: "Playlists");

            migrationBuilder.DropColumn(
                name: "SupportsLevelVoting",
                table: "Playlists");

            migrationBuilder.DropColumn(
                name: "SupportsScreens",
                table: "Playlists");

            migrationBuilder.DropColumn(
                name: "SupportsTeleportVR",
                table: "Playlists");

            migrationBuilder.DropColumn(
                name: "SupportsWalkVR",
                table: "Playlists");

            migrationBuilder.DropColumn(
                name: "WarningMask",
                table: "Playlists");
        }
    }
}
