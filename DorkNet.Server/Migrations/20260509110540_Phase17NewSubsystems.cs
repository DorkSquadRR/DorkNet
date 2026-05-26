using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DorkNet.Server.Migrations
{
    /// <inheritdoc />
    public partial class Phase17NewSubsystems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "TargetEventId",
                table: "Reports",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "TargetInventionId",
                table: "Reports",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "TargetRoomId",
                table: "Reports",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "CreationRoomId",
                table: "Inventions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CreatorPermission",
                table: "Inventions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "CurrentVersionNumber",
                table: "Inventions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "FirstPublishedAt",
                table: "Inventions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GeneralPermission",
                table: "Inventions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsAgInvention",
                table: "Inventions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "Inventions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsPublished",
                table: "Inventions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "NumPlayersHaveUsedInRoom",
                table: "Inventions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ReplicationId",
                table: "Inventions",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "TargetInventionId",
                table: "Cheers",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "BugReports",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ReporterPlayerId = table.Column<long>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Body = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    GameSessionId = table.Column<long>(type: "INTEGER", nullable: false),
                    ClientVersion = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Platform = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ReadAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BugReports", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ClubMemberships",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ClubId = table.Column<long>(type: "INTEGER", nullable: false),
                    PlayerId = table.Column<long>(type: "INTEGER", nullable: false),
                    Permissions = table.Column<int>(type: "INTEGER", nullable: false),
                    JoinedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClubMemberships", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Clubs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false, collation: "NOCASE"),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    CreatorPlayerId = table.Column<long>(type: "INTEGER", nullable: false),
                    ImageName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    BanStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Clubs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CohortAssignments",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PlayerId = table.Column<long>(type: "INTEGER", nullable: false),
                    CohortKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Variant = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CohortAssignments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CouponRedemptions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CouponId = table.Column<long>(type: "INTEGER", nullable: false),
                    PlayerId = table.Column<long>(type: "INTEGER", nullable: false),
                    RedeemedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CouponRedemptions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Coupons",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Code = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    RewardType = table.Column<int>(type: "INTEGER", nullable: false),
                    RewardCurrencyType = table.Column<int>(type: "INTEGER", nullable: false),
                    RewardAmount = table.Column<long>(type: "INTEGER", nullable: false),
                    RewardItemSlug = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    MaxRedemptions = table.Column<int>(type: "INTEGER", nullable: false),
                    RedemptionCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Coupons", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GiftPackages",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RecipientPlayerId = table.Column<long>(type: "INTEGER", nullable: false),
                    FromPlayerId = table.Column<int>(type: "INTEGER", nullable: true),
                    ConsumableItemDesc = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    AvatarItemType = table.Column<int>(type: "INTEGER", nullable: true),
                    AvatarItemDescOrHairDyeDesc = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    EquipmentPrefabName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    EquipmentModificationGuid = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CurrencyType = table.Column<int>(type: "INTEGER", nullable: false),
                    Currency = table.Column<int>(type: "INTEGER", nullable: false),
                    Xp = table.Column<int>(type: "INTEGER", nullable: false),
                    Level = table.Column<int>(type: "INTEGER", nullable: false),
                    GiftContext = table.Column<int>(type: "INTEGER", nullable: false),
                    GiftRarity = table.Column<int>(type: "INTEGER", nullable: false),
                    Message = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Platform = table.Column<int>(type: "INTEGER", nullable: false),
                    PackageMaterial = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    PackageVariant = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Consumed = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsValid = table.Column<bool>(type: "INTEGER", nullable: false),
                    ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    SupportsCurrentPlatform = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsGifted = table.Column<bool>(type: "INTEGER", nullable: false),
                    ConsumedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GiftPackages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InventionVersions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    InventionId = table.Column<long>(type: "INTEGER", nullable: false),
                    ReplicationId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    VersionNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    BlobName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    InstantiationCost = table.Column<int>(type: "INTEGER", nullable: false),
                    LightsCost = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventionVersions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NotificationPrefs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PlayerId = table.Column<long>(type: "INTEGER", nullable: false),
                    Platform = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    AllowAll = table.Column<bool>(type: "INTEGER", nullable: false),
                    AllowFriendRequest = table.Column<bool>(type: "INTEGER", nullable: false),
                    AllowMessage = table.Column<bool>(type: "INTEGER", nullable: false),
                    AllowEventInvite = table.Column<bool>(type: "INTEGER", nullable: false),
                    AllowAnnouncements = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationPrefs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlayerDevices",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PlayerId = table.Column<long>(type: "INTEGER", nullable: false),
                    DeviceId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Platform = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    FirstSeen = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastSeen = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsBanned = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerDevices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlayerInventory",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PlayerId = table.Column<long>(type: "INTEGER", nullable: false),
                    ItemSlug = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    AcquiredAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerInventory", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RoomBans",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RoomId = table.Column<long>(type: "INTEGER", nullable: false),
                    BannedPlayerId = table.Column<long>(type: "INTEGER", nullable: false),
                    BannedByPlayerId = table.Column<long>(type: "INTEGER", nullable: false),
                    BanType = table.Column<int>(type: "INTEGER", nullable: false),
                    Until = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoomBans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RoyaleMatches",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Rank = table.Column<int>(type: "INTEGER", nullable: false),
                    NumEliminations = table.Column<int>(type: "INTEGER", nullable: false),
                    SecondsAlive = table.Column<int>(type: "INTEGER", nullable: false),
                    WalkGame = table.Column<bool>(type: "INTEGER", nullable: false),
                    CustomGame = table.Column<bool>(type: "INTEGER", nullable: false),
                    ChestsOpened = table.Column<int>(type: "INTEGER", nullable: false),
                    ShieldPotionsConsumed = table.Column<int>(type: "INTEGER", nullable: false),
                    HealthPotionsConsumed = table.Column<int>(type: "INTEGER", nullable: false),
                    SecondsInAir = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoyaleMatches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RoyaleMatchPlayers",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MatchId = table.Column<long>(type: "INTEGER", nullable: false),
                    PlayerId = table.Column<long>(type: "INTEGER", nullable: false),
                    Rank = table.Column<int>(type: "INTEGER", nullable: false),
                    NumEliminations = table.Column<int>(type: "INTEGER", nullable: false),
                    SecondsAlive = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoyaleMatchPlayers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RoyalePlayerProgress",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PlayerId = table.Column<long>(type: "INTEGER", nullable: false),
                    TotalXP = table.Column<long>(type: "INTEGER", nullable: false),
                    Level = table.Column<int>(type: "INTEGER", nullable: false),
                    RankIdx = table.Column<int>(type: "INTEGER", nullable: false),
                    RankName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CurrentLevelXPThreshold = table.Column<long>(type: "INTEGER", nullable: false),
                    NextLevelXPThreshold = table.Column<long>(type: "INTEGER", nullable: false),
                    NextLevelAcornReward = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoyalePlayerProgress", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TestCases",
                columns: table => new
                {
                    Pk = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Key = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    RoomName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    MinNumAssignedPlayers = table.Column<int>(type: "INTEGER", nullable: false),
                    AssignedPlayerIdsJson = table.Column<string>(type: "TEXT", nullable: false),
                    AssignedPlayerNamesJson = table.Column<string>(type: "TEXT", nullable: false),
                    TagsJson = table.Column<string>(type: "TEXT", nullable: false),
                    JiraUrl = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    JiraBugUrl = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    TestPassId = table.Column<uint>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestCases", x => x.Pk);
                });

            migrationBuilder.CreateTable(
                name: "TestPasses",
                columns: table => new
                {
                    Id = table.Column<uint>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    StartDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    WasManuallyClosed = table.Column<bool>(type: "INTEGER", nullable: false),
                    TagsJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestPasses", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BugReports_ReadAt",
                table: "BugReports",
                column: "ReadAt");

            migrationBuilder.CreateIndex(
                name: "IX_BugReports_ReporterPlayerId",
                table: "BugReports",
                column: "ReporterPlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_ClubMemberships_ClubId_PlayerId",
                table: "ClubMemberships",
                columns: new[] { "ClubId", "PlayerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClubMemberships_PlayerId",
                table: "ClubMemberships",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_Clubs_CreatorPlayerId",
                table: "Clubs",
                column: "CreatorPlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_Clubs_Name",
                table: "Clubs",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CohortAssignments_PlayerId_CohortKey",
                table: "CohortAssignments",
                columns: new[] { "PlayerId", "CohortKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CouponRedemptions_CouponId_PlayerId",
                table: "CouponRedemptions",
                columns: new[] { "CouponId", "PlayerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Coupons_Code",
                table: "Coupons",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GiftPackages_RecipientPlayerId_Consumed",
                table: "GiftPackages",
                columns: new[] { "RecipientPlayerId", "Consumed" });

            migrationBuilder.CreateIndex(
                name: "IX_InventionVersions_InventionId_VersionNumber",
                table: "InventionVersions",
                columns: new[] { "InventionId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotificationPrefs_PlayerId_Platform",
                table: "NotificationPrefs",
                columns: new[] { "PlayerId", "Platform" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerDevices_DeviceId",
                table: "PlayerDevices",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerDevices_PlayerId",
                table: "PlayerDevices",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerInventory_PlayerId",
                table: "PlayerInventory",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerInventory_PlayerId_ItemSlug",
                table: "PlayerInventory",
                columns: new[] { "PlayerId", "ItemSlug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RoomBans_BannedPlayerId",
                table: "RoomBans",
                column: "BannedPlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_RoomBans_RoomId_BannedPlayerId",
                table: "RoomBans",
                columns: new[] { "RoomId", "BannedPlayerId" });

            migrationBuilder.CreateIndex(
                name: "IX_RoyaleMatches_CompletedAt",
                table: "RoyaleMatches",
                column: "CompletedAt");

            migrationBuilder.CreateIndex(
                name: "IX_RoyaleMatchPlayers_MatchId_PlayerId",
                table: "RoyaleMatchPlayers",
                columns: new[] { "MatchId", "PlayerId" });

            migrationBuilder.CreateIndex(
                name: "IX_RoyaleMatchPlayers_PlayerId",
                table: "RoyaleMatchPlayers",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_RoyalePlayerProgress_PlayerId",
                table: "RoyalePlayerProgress",
                column: "PlayerId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TestCases_Id",
                table: "TestCases",
                column: "Id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TestCases_Status",
                table: "TestCases",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_TestCases_TestPassId",
                table: "TestCases",
                column: "TestPassId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BugReports");

            migrationBuilder.DropTable(
                name: "ClubMemberships");

            migrationBuilder.DropTable(
                name: "Clubs");

            migrationBuilder.DropTable(
                name: "CohortAssignments");

            migrationBuilder.DropTable(
                name: "CouponRedemptions");

            migrationBuilder.DropTable(
                name: "Coupons");

            migrationBuilder.DropTable(
                name: "GiftPackages");

            migrationBuilder.DropTable(
                name: "InventionVersions");

            migrationBuilder.DropTable(
                name: "NotificationPrefs");

            migrationBuilder.DropTable(
                name: "PlayerDevices");

            migrationBuilder.DropTable(
                name: "PlayerInventory");

            migrationBuilder.DropTable(
                name: "RoomBans");

            migrationBuilder.DropTable(
                name: "RoyaleMatches");

            migrationBuilder.DropTable(
                name: "RoyaleMatchPlayers");

            migrationBuilder.DropTable(
                name: "RoyalePlayerProgress");

            migrationBuilder.DropTable(
                name: "TestCases");

            migrationBuilder.DropTable(
                name: "TestPasses");

            migrationBuilder.DropColumn(
                name: "TargetEventId",
                table: "Reports");

            migrationBuilder.DropColumn(
                name: "TargetInventionId",
                table: "Reports");

            migrationBuilder.DropColumn(
                name: "TargetRoomId",
                table: "Reports");

            migrationBuilder.DropColumn(
                name: "CreationRoomId",
                table: "Inventions");

            migrationBuilder.DropColumn(
                name: "CreatorPermission",
                table: "Inventions");

            migrationBuilder.DropColumn(
                name: "CurrentVersionNumber",
                table: "Inventions");

            migrationBuilder.DropColumn(
                name: "FirstPublishedAt",
                table: "Inventions");

            migrationBuilder.DropColumn(
                name: "GeneralPermission",
                table: "Inventions");

            migrationBuilder.DropColumn(
                name: "IsAgInvention",
                table: "Inventions");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "Inventions");

            migrationBuilder.DropColumn(
                name: "IsPublished",
                table: "Inventions");

            migrationBuilder.DropColumn(
                name: "NumPlayersHaveUsedInRoom",
                table: "Inventions");

            migrationBuilder.DropColumn(
                name: "ReplicationId",
                table: "Inventions");

            migrationBuilder.DropColumn(
                name: "TargetInventionId",
                table: "Cheers");
        }
    }
}
