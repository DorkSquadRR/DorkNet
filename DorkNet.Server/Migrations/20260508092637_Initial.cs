using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DorkNet.Server.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Players",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DeviceId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    LastPlatform = table.Column<int>(type: "INTEGER", nullable: false),
                    LastPlatformId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Username = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Bio = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Level = table.Column<int>(type: "INTEGER", nullable: false),
                    XP = table.Column<int>(type: "INTEGER", nullable: false),
                    Reputation = table.Column<int>(type: "INTEGER", nullable: false),
                    IsVerified = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsDeveloper = table.Column<bool>(type: "INTEGER", nullable: false),
                    CanReceiveInvites = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsJunior = table.Column<bool>(type: "INTEGER", nullable: false),
                    ProfileImageName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Players", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RoomBookmarks",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PlayerId = table.Column<long>(type: "INTEGER", nullable: false),
                    RoomId = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoomBookmarks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Rooms",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false, collation: "NOCASE"),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    CreatorPlayerId = table.Column<long>(type: "INTEGER", nullable: false),
                    ImageName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    Accessibility = table.Column<int>(type: "INTEGER", nullable: false),
                    SupportsLevelVoting = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsAGRoom = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsDormRoom = table.Column<bool>(type: "INTEGER", nullable: false),
                    CloningAllowed = table.Column<bool>(type: "INTEGER", nullable: false),
                    SupportsVRLow = table.Column<bool>(type: "INTEGER", nullable: false),
                    SupportsMobile = table.Column<bool>(type: "INTEGER", nullable: false),
                    SupportsScreens = table.Column<bool>(type: "INTEGER", nullable: false),
                    SupportsWalkVR = table.Column<bool>(type: "INTEGER", nullable: false),
                    SupportsTeleportVR = table.Column<bool>(type: "INTEGER", nullable: false),
                    AllowsJuniors = table.Column<bool>(type: "INTEGER", nullable: false),
                    RoomWarningMask = table.Column<int>(type: "INTEGER", nullable: false),
                    CustomRoomWarning = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    DisableMicAutoMute = table.Column<bool>(type: "INTEGER", nullable: false),
                    LocationReplicationId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    TagsCsv = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    CheerCount = table.Column<int>(type: "INTEGER", nullable: false),
                    FavoriteCount = table.Column<int>(type: "INTEGER", nullable: false),
                    VisitCount = table.Column<int>(type: "INTEGER", nullable: false),
                    HotScore = table.Column<double>(type: "REAL", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Rooms", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Avatars",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PlayerId = table.Column<long>(type: "INTEGER", nullable: false),
                    EquippedItemsJson = table.Column<string>(type: "TEXT", nullable: false),
                    InventoryJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Avatars", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Avatars_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlayerSettings",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PlayerId = table.Column<long>(type: "INTEGER", nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Value = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlayerSettings_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Relationships",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RequesterId = table.Column<long>(type: "INTEGER", nullable: false),
                    TargetId = table.Column<long>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Relationships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Relationships_Players_RequesterId",
                        column: x => x.RequesterId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Avatars_PlayerId",
                table: "Avatars",
                column: "PlayerId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Players_Username",
                table: "Players",
                column: "Username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerSettings_PlayerId_Key",
                table: "PlayerSettings",
                columns: new[] { "PlayerId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Relationships_RequesterId_TargetId",
                table: "Relationships",
                columns: new[] { "RequesterId", "TargetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RoomBookmarks_PlayerId",
                table: "RoomBookmarks",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_RoomBookmarks_PlayerId_RoomId",
                table: "RoomBookmarks",
                columns: new[] { "PlayerId", "RoomId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Rooms_CreatorPlayerId",
                table: "Rooms",
                column: "CreatorPlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_Rooms_HotScore",
                table: "Rooms",
                column: "HotScore");

            migrationBuilder.CreateIndex(
                name: "IX_Rooms_Name",
                table: "Rooms",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Avatars");

            migrationBuilder.DropTable(
                name: "PlayerSettings");

            migrationBuilder.DropTable(
                name: "Relationships");

            migrationBuilder.DropTable(
                name: "RoomBookmarks");

            migrationBuilder.DropTable(
                name: "Rooms");

            migrationBuilder.DropTable(
                name: "Players");
        }
    }
}
