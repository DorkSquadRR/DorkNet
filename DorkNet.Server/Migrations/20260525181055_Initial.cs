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
                name: "AdminActions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AdminPlayerId = table.Column<long>(type: "INTEGER", nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    TargetType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    TargetId = table.Column<long>(type: "INTEGER", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminActions", x => x.Id);
                });

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
                name: "Cards",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PlayerId = table.Column<long>(type: "INTEGER", nullable: true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Body = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    ImageName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ActionUrl = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cards", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ChatMessages",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ThreadKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SenderPlayerId = table.Column<long>(type: "INTEGER", nullable: false),
                    Body = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    SentAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ChatThreadMembers",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ThreadKey = table.Column<string>(type: "TEXT", nullable: false),
                    PlayerId = table.Column<long>(type: "INTEGER", nullable: false),
                    JoinedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SnoozeUntil = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastReadMessageId = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatThreadMembers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ChatThreads",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ThreadKey = table.Column<string>(type: "TEXT", maxLength: 96, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    CreatorPlayerId = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatThreads", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Cheers",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FromPlayerId = table.Column<long>(type: "INTEGER", nullable: false),
                    TargetPlayerId = table.Column<long>(type: "INTEGER", nullable: false),
                    TargetRoomId = table.Column<long>(type: "INTEGER", nullable: false),
                    TargetPhotoId = table.Column<long>(type: "INTEGER", nullable: false),
                    TargetInventionId = table.Column<long>(type: "INTEGER", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    CheeredAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cheers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ClubAnnouncementReads",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AnnouncementId = table.Column<long>(type: "INTEGER", nullable: false),
                    PlayerId = table.Column<long>(type: "INTEGER", nullable: false),
                    ReadAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClubAnnouncementReads", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ClubAnnouncements",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ClubId = table.Column<long>(type: "INTEGER", nullable: false),
                    AuthorPlayerId = table.Column<long>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    ImageName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClubAnnouncements", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ClubCategoryAssignments",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ClubId = table.Column<long>(type: "INTEGER", nullable: false),
                    CategoryTagId = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClubCategoryAssignments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ClubCategoryTags",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    OrderIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    Active = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClubCategoryTags", x => x.Id);
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
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Visibility = table.Column<int>(type: "INTEGER", nullable: false),
                    Joinability = table.Column<int>(type: "INTEGER", nullable: false),
                    AllowJuniors = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsRRO = table.Column<bool>(type: "INTEGER", nullable: false),
                    ClubhouseRoomId = table.Column<long>(type: "INTEGER", nullable: true),
                    ClubType = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Clubs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ClubSubscriptions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ClubId = table.Column<long>(type: "INTEGER", nullable: false),
                    PlayerId = table.Column<long>(type: "INTEGER", nullable: false),
                    SubscribedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClubSubscriptions", x => x.Id);
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
                name: "CommunityBoardRows",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    Json = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommunityBoardRows", x => x.Id);
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
                name: "CurrencyBalances",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PlayerId = table.Column<long>(type: "INTEGER", nullable: false),
                    CurrencyType = table.Column<int>(type: "INTEGER", nullable: false),
                    Balance = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CurrencyBalances", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DormStates",
                columns: table => new
                {
                    PlayerId = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CurrentDataBlobName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DormStates", x => x.PlayerId);
                });

            migrationBuilder.CreateTable(
                name: "GameRewardSelections",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PlayerId = table.Column<long>(type: "INTEGER", nullable: false),
                    RewardType = table.Column<int>(type: "INTEGER", nullable: false),
                    GiftContext = table.Column<int>(type: "INTEGER", nullable: false),
                    Message = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SelectedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SelectedGiftDropId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameRewardSelections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GameSessions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RoomId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ActivityLevelId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Region = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    PhotonRoomName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    MaxCapacity = table.Column<int>(type: "INTEGER", nullable: false),
                    PlayerCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameSessions", x => x.Id);
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
                name: "Inventions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CreatorPlayerId = table.Column<long>(type: "INTEGER", nullable: false),
                    ReplicationId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    ImageName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Permission = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatorPermission = table.Column<int>(type: "INTEGER", nullable: false),
                    GeneralPermission = table.Column<int>(type: "INTEGER", nullable: false),
                    IsPublished = table.Column<bool>(type: "INTEGER", nullable: false),
                    CurrentVersionNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    FirstPublishedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreationRoomId = table.Column<long>(type: "INTEGER", nullable: true),
                    NumPlayersHaveUsedInRoom = table.Column<int>(type: "INTEGER", nullable: false),
                    IsAgInvention = table.Column<bool>(type: "INTEGER", nullable: false),
                    CurrentBlobName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    TagsCsv = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    CheerCount = table.Column<int>(type: "INTEGER", nullable: false),
                    SpawnCount = table.Column<int>(type: "INTEGER", nullable: false),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Inventions", x => x.Id);
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
                name: "IpBans",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Cidr = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    BannedByAdminId = table.Column<long>(type: "INTEGER", nullable: false),
                    BannedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Until = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IpBans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LeaderboardChannelMeta",
                columns: table => new
                {
                    Channel = table.Column<int>(type: "INTEGER", nullable: false),
                    RoomId = table.Column<long>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    LowerIsBetter = table.Column<bool>(type: "INTEGER", nullable: false),
                    ValueFormat = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaderboardChannelMeta", x => new { x.RoomId, x.Channel });
                });

            migrationBuilder.CreateTable(
                name: "LeaderboardStats",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PlayerId = table.Column<long>(type: "INTEGER", nullable: false),
                    RoomId = table.Column<long>(type: "INTEGER", nullable: false),
                    StatChannel = table.Column<int>(type: "INTEGER", nullable: false),
                    Value = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaderboardStats", x => x.Id);
                });

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

            migrationBuilder.CreateTable(
                name: "Messages",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SenderPlayerId = table.Column<long>(type: "INTEGER", nullable: false),
                    RecipientPlayerId = table.Column<long>(type: "INTEGER", nullable: false),
                    Body = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    RoomId = table.Column<long>(type: "INTEGER", nullable: true),
                    SentAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ReadAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Messages", x => x.Id);
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
                name: "ObjectiveProgress",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PlayerId = table.Column<long>(type: "INTEGER", nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    IsCompleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    ClearedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ObjectiveProgress", x => x.Id);
                });

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

            migrationBuilder.CreateTable(
                name: "PlatformIgnores",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PlayerId = table.Column<long>(type: "INTEGER", nullable: false),
                    Platform = table.Column<int>(type: "INTEGER", nullable: false),
                    PlatformUserId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformIgnores", x => x.Id);
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
                name: "PlayerElo",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PlayerId = table.Column<long>(type: "INTEGER", nullable: false),
                    GameMode = table.Column<int>(type: "INTEGER", nullable: false),
                    Elo = table.Column<int>(type: "INTEGER", nullable: false),
                    Wins = table.Column<int>(type: "INTEGER", nullable: false),
                    Losses = table.Column<int>(type: "INTEGER", nullable: false),
                    MatchesPlayed = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerElo", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlayerEventResponses",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EventId = table.Column<long>(type: "INTEGER", nullable: false),
                    PlayerId = table.Column<long>(type: "INTEGER", nullable: false),
                    Response = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerEventResponses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlayerEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CreatorPlayerId = table.Column<long>(type: "INTEGER", nullable: false),
                    RoomId = table.Column<long>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    StartsAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndsAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Capacity = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlayerInventory",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PlayerId = table.Column<long>(type: "INTEGER", nullable: false),
                    ItemSlug = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    AcquiredAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerInventory", x => x.Id);
                });

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
                    IsCommunityTeam = table.Column<bool>(type: "INTEGER", nullable: false),
                    CanReceiveInvites = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsJunior = table.Column<bool>(type: "INTEGER", nullable: false),
                    ProfileImageName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Email = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    BannedUntil = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsAdmin = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastIp = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    CreatedFromDeviceId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Locale = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    PasswordHash = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Birthday = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Phone = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Players", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlaylistInteractions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PlaylistId = table.Column<long>(type: "INTEGER", nullable: false),
                    PlayerId = table.Column<long>(type: "INTEGER", nullable: false),
                    Cheered = table.Column<bool>(type: "INTEGER", nullable: false),
                    Favorited = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaylistInteractions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlaylistRooms",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PlaylistId = table.Column<long>(type: "INTEGER", nullable: false),
                    RoomId = table.Column<long>(type: "INTEGER", nullable: false),
                    OrderIndex = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaylistRooms", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Playlists",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false, collation: "NOCASE"),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    ImageName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    CreatorPlayerId = table.Column<long>(type: "INTEGER", nullable: false),
                    IsCurated = table.Column<bool>(type: "INTEGER", nullable: false),
                    OrderIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    TagsCsv = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    CheerCount = table.Column<int>(type: "INTEGER", nullable: false),
                    FavoriteCount = table.Column<int>(type: "INTEGER", nullable: false),
                    VisitorCount = table.Column<int>(type: "INTEGER", nullable: false),
                    VisitCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Playlists", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PrivateInstanceInvitees",
                columns: table => new
                {
                    PrivateInstanceId = table.Column<long>(type: "INTEGER", nullable: false),
                    PlayerId = table.Column<long>(type: "INTEGER", nullable: false),
                    InvitedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LatestInviteMessageId = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrivateInstanceInvitees", x => new { x.PrivateInstanceId, x.PlayerId });
                });

            migrationBuilder.CreateTable(
                name: "PrivateInstances",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false),
                    RoomId = table.Column<long>(type: "INTEGER", nullable: false),
                    SubRoomId = table.Column<long>(type: "INTEGER", nullable: false),
                    OwnerPlayerId = table.Column<long>(type: "INTEGER", nullable: false),
                    PhotonRoomId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Location = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    DataBlob = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    PhotonRegion = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    MaxCapacity = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrivateInstances", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PushTokens",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PlayerId = table.Column<long>(type: "INTEGER", nullable: false),
                    Platform = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Token = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PushTokens", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Reports",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ReporterPlayerId = table.Column<long>(type: "INTEGER", nullable: false),
                    TargetPlayerId = table.Column<long>(type: "INTEGER", nullable: false),
                    Category = table.Column<int>(type: "INTEGER", nullable: false),
                    Message = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    GameSessionId = table.Column<long>(type: "INTEGER", nullable: false),
                    RoomId = table.Column<long>(type: "INTEGER", nullable: false),
                    TargetInventionId = table.Column<long>(type: "INTEGER", nullable: false),
                    TargetRoomId = table.Column<long>(type: "INTEGER", nullable: false),
                    TargetEventId = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ResolverAdminId = table.Column<long>(type: "INTEGER", nullable: true),
                    ResolutionNote = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reports", x => x.Id);
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
                name: "RoomDataBlobs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RoomId = table.Column<long>(type: "INTEGER", nullable: false),
                    BlobName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    UploadedByPlayerId = table.Column<long>(type: "INTEGER", nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SubRoomId = table.Column<long>(type: "INTEGER", nullable: true),
                    ReferencedFilenamesCsv = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    Bytes = table.Column<byte[]>(type: "BLOB", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoomDataBlobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RoomKeyPurchases",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RoomKeyId = table.Column<long>(type: "INTEGER", nullable: false),
                    PlayerId = table.Column<long>(type: "INTEGER", nullable: false),
                    PaidPrice = table.Column<int>(type: "INTEGER", nullable: false),
                    PurchasedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoomKeyPurchases", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RoomKeys",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ReplicationId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    RoomId = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatorPlayerId = table.Column<long>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 174, nullable: false),
                    Price = table.Column<int>(type: "INTEGER", nullable: false),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoomKeys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RoomRoles",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RoomId = table.Column<long>(type: "INTEGER", nullable: false),
                    PlayerId = table.Column<long>(type: "INTEGER", nullable: false),
                    Role = table.Column<int>(type: "INTEGER", nullable: false),
                    Accepted = table.Column<bool>(type: "INTEGER", nullable: false),
                    GrantedByPlayerId = table.Column<long>(type: "INTEGER", nullable: true),
                    GrantedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoomRoles", x => x.Id);
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
                    VisitorCount = table.Column<int>(type: "INTEGER", nullable: false),
                    HotScore = table.Column<double>(type: "REAL", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    HiddenFromBrowse = table.Column<bool>(type: "INTEGER", nullable: false),
                    CurrentDataBlobName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Rooms", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RoomScenes",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RoomId = table.Column<long>(type: "INTEGER", nullable: false),
                    OrderIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false, collation: "NOCASE"),
                    RoomSceneLocationId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    DataBlobName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    MaxPlayers = table.Column<int>(type: "INTEGER", nullable: false),
                    IsSandbox = table.Column<bool>(type: "INTEGER", nullable: false),
                    CanMatchmakeInto = table.Column<bool>(type: "INTEGER", nullable: false),
                    DataModifiedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoomScenes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RoomVisits",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RoomId = table.Column<long>(type: "INTEGER", nullable: false),
                    PlayerId = table.Column<long>(type: "INTEGER", nullable: false),
                    FirstVisitAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastVisitAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    VisitCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoomVisits", x => x.Id);
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
                name: "ServerSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    SignupsDisabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    WeeklyChallengesCompletedRequired = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    WeeklyChallengesJson = table.Column<string>(type: "TEXT", nullable: false, defaultValue: ""),
                    WeeklyChallengeRewardJson = table.Column<string>(type: "TEXT", nullable: false, defaultValue: ""),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServerSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StoreItems",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Slug = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
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

            migrationBuilder.CreateTable(
                name: "Subscriptions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SubscriberPlayerId = table.Column<long>(type: "INTEGER", nullable: false),
                    TargetPlayerId = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Subscriptions", x => x.Id);
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

            migrationBuilder.CreateTable(
                name: "Avatars",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PlayerId = table.Column<long>(type: "INTEGER", nullable: false),
                    EquippedItemsJson = table.Column<string>(type: "TEXT", nullable: false),
                    InventoryJson = table.Column<string>(type: "TEXT", nullable: false),
                    OutfitSelections = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    FaceFeatures = table.Column<string>(type: "TEXT", maxLength: 8192, nullable: false),
                    HairColor = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SkinColor = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SavedOutfitsJson = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
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
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Favorited = table.Column<bool>(type: "INTEGER", nullable: false),
                    Muted = table.Column<bool>(type: "INTEGER", nullable: false),
                    Ignored = table.Column<bool>(type: "INTEGER", nullable: false)
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
                name: "IX_AdminActions_AdminPlayerId",
                table: "AdminActions",
                column: "AdminPlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_AdminActions_TargetType_TargetId",
                table: "AdminActions",
                columns: new[] { "TargetType", "TargetId" });

            migrationBuilder.CreateIndex(
                name: "IX_AdminActions_Timestamp",
                table: "AdminActions",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_Avatars_PlayerId",
                table: "Avatars",
                column: "PlayerId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BugReports_ReadAt",
                table: "BugReports",
                column: "ReadAt");

            migrationBuilder.CreateIndex(
                name: "IX_BugReports_ReporterPlayerId",
                table: "BugReports",
                column: "ReporterPlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_Cards_ExpiresAt",
                table: "Cards",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_Cards_PlayerId",
                table: "Cards",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_ThreadKey_SentAt",
                table: "ChatMessages",
                columns: new[] { "ThreadKey", "SentAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatThreadMembers_PlayerId",
                table: "ChatThreadMembers",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatThreadMembers_ThreadKey_PlayerId",
                table: "ChatThreadMembers",
                columns: new[] { "ThreadKey", "PlayerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChatThreads_ThreadKey",
                table: "ChatThreads",
                column: "ThreadKey",
                unique: true);

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
                name: "IX_Cheers_TargetPlayerId",
                table: "Cheers",
                column: "TargetPlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_Cheers_TargetRoomId",
                table: "Cheers",
                column: "TargetRoomId");

            migrationBuilder.CreateIndex(
                name: "IX_ClubAnnouncementReads_AnnouncementId_PlayerId",
                table: "ClubAnnouncementReads",
                columns: new[] { "AnnouncementId", "PlayerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClubAnnouncementReads_PlayerId",
                table: "ClubAnnouncementReads",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_ClubAnnouncements_ClubId_CreatedAt",
                table: "ClubAnnouncements",
                columns: new[] { "ClubId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ClubCategoryAssignments_CategoryTagId",
                table: "ClubCategoryAssignments",
                column: "CategoryTagId");

            migrationBuilder.CreateIndex(
                name: "IX_ClubCategoryAssignments_ClubId_CategoryTagId",
                table: "ClubCategoryAssignments",
                columns: new[] { "ClubId", "CategoryTagId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClubCategoryTags_Name",
                table: "ClubCategoryTags",
                column: "Name");

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
                name: "IX_ClubSubscriptions_ClubId",
                table: "ClubSubscriptions",
                column: "ClubId");

            migrationBuilder.CreateIndex(
                name: "IX_ClubSubscriptions_PlayerId_ClubId",
                table: "ClubSubscriptions",
                columns: new[] { "PlayerId", "ClubId" },
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
                name: "IX_CurrencyBalances_PlayerId_CurrencyType",
                table: "CurrencyBalances",
                columns: new[] { "PlayerId", "CurrencyType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GameRewardSelections_PlayerId_SelectedAt",
                table: "GameRewardSelections",
                columns: new[] { "PlayerId", "SelectedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_GameSessions_RoomId_Region",
                table: "GameSessions",
                columns: new[] { "RoomId", "Region" });

            migrationBuilder.CreateIndex(
                name: "IX_GiftPackages_RecipientPlayerId_Consumed",
                table: "GiftPackages",
                columns: new[] { "RecipientPlayerId", "Consumed" });

            migrationBuilder.CreateIndex(
                name: "IX_Inventions_CheerCount",
                table: "Inventions",
                column: "CheerCount");

            migrationBuilder.CreateIndex(
                name: "IX_Inventions_CreatorPlayerId",
                table: "Inventions",
                column: "CreatorPlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_InventionVersions_InventionId_VersionNumber",
                table: "InventionVersions",
                columns: new[] { "InventionId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IpBans_Cidr",
                table: "IpBans",
                column: "Cidr");

            migrationBuilder.CreateIndex(
                name: "IX_LeaderboardChannelMeta_RoomId",
                table: "LeaderboardChannelMeta",
                column: "RoomId");

            migrationBuilder.CreateIndex(
                name: "IX_LeaderboardStats_RoomId_PlayerId_StatChannel",
                table: "LeaderboardStats",
                columns: new[] { "RoomId", "PlayerId", "StatChannel" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeaderboardStats_RoomId_StatChannel_Value",
                table: "LeaderboardStats",
                columns: new[] { "RoomId", "StatChannel", "Value" });

            migrationBuilder.CreateIndex(
                name: "IX_Messages_RecipientPlayerId_SentAt",
                table: "Messages",
                columns: new[] { "RecipientPlayerId", "SentAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Messages_SenderPlayerId_SentAt",
                table: "Messages",
                columns: new[] { "SenderPlayerId", "SentAt" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationPrefs_PlayerId_Platform",
                table: "NotificationPrefs",
                columns: new[] { "PlayerId", "Platform" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ObjectiveProgress_PlayerId_Key",
                table: "ObjectiveProgress",
                columns: new[] { "PlayerId", "Key" },
                unique: true);

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

            migrationBuilder.CreateIndex(
                name: "IX_PlatformIgnores_PlayerId_Platform_PlatformUserId",
                table: "PlatformIgnores",
                columns: new[] { "PlayerId", "Platform", "PlatformUserId" },
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
                name: "IX_PlayerElo_GameMode_Elo",
                table: "PlayerElo",
                columns: new[] { "GameMode", "Elo" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerElo_PlayerId_GameMode",
                table: "PlayerElo",
                columns: new[] { "PlayerId", "GameMode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerEventResponses_EventId_PlayerId",
                table: "PlayerEventResponses",
                columns: new[] { "EventId", "PlayerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerEventResponses_PlayerId",
                table: "PlayerEventResponses",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerEvents_CreatorPlayerId",
                table: "PlayerEvents",
                column: "CreatorPlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerEvents_StartsAt",
                table: "PlayerEvents",
                column: "StartsAt");

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
                name: "IX_PlaylistInteractions_PlayerId",
                table: "PlaylistInteractions",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_PlaylistInteractions_PlaylistId_PlayerId",
                table: "PlaylistInteractions",
                columns: new[] { "PlaylistId", "PlayerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlaylistRooms_PlaylistId",
                table: "PlaylistRooms",
                column: "PlaylistId");

            migrationBuilder.CreateIndex(
                name: "IX_PlaylistRooms_PlaylistId_OrderIndex",
                table: "PlaylistRooms",
                columns: new[] { "PlaylistId", "OrderIndex" });

            migrationBuilder.CreateIndex(
                name: "IX_PlaylistRooms_RoomId",
                table: "PlaylistRooms",
                column: "RoomId");

            migrationBuilder.CreateIndex(
                name: "IX_Playlists_CreatorPlayerId",
                table: "Playlists",
                column: "CreatorPlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_Playlists_IsCurated",
                table: "Playlists",
                column: "IsCurated");

            migrationBuilder.CreateIndex(
                name: "IX_Playlists_Name",
                table: "Playlists",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_PrivateInstanceInvitees_PlayerId",
                table: "PrivateInstanceInvitees",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_PrivateInstances_OwnerPlayerId",
                table: "PrivateInstances",
                column: "OwnerPlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_PrivateInstances_RoomId_SubRoomId",
                table: "PrivateInstances",
                columns: new[] { "RoomId", "SubRoomId" });

            migrationBuilder.CreateIndex(
                name: "IX_PushTokens_PlayerId_Platform",
                table: "PushTokens",
                columns: new[] { "PlayerId", "Platform" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PushTokens_Token",
                table: "PushTokens",
                column: "Token");

            migrationBuilder.CreateIndex(
                name: "IX_Relationships_RequesterId_TargetId",
                table: "Relationships",
                columns: new[] { "RequesterId", "TargetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Reports_ReporterPlayerId",
                table: "Reports",
                column: "ReporterPlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_Reports_ResolvedAt",
                table: "Reports",
                column: "ResolvedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Reports_TargetPlayerId",
                table: "Reports",
                column: "TargetPlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_RoomBans_BannedPlayerId",
                table: "RoomBans",
                column: "BannedPlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_RoomBans_RoomId_BannedPlayerId",
                table: "RoomBans",
                columns: new[] { "RoomId", "BannedPlayerId" });

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
                name: "IX_RoomDataBlobs_BlobName",
                table: "RoomDataBlobs",
                column: "BlobName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RoomDataBlobs_RoomId",
                table: "RoomDataBlobs",
                column: "RoomId");

            migrationBuilder.CreateIndex(
                name: "IX_RoomKeyPurchases_PlayerId",
                table: "RoomKeyPurchases",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_RoomKeyPurchases_RoomKeyId_PlayerId",
                table: "RoomKeyPurchases",
                columns: new[] { "RoomKeyId", "PlayerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RoomKeys_CreatorPlayerId",
                table: "RoomKeys",
                column: "CreatorPlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_RoomKeys_RoomId",
                table: "RoomKeys",
                column: "RoomId");

            migrationBuilder.CreateIndex(
                name: "IX_RoomKeys_RoomId_Name",
                table: "RoomKeys",
                columns: new[] { "RoomId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_RoomRoles_RoomId",
                table: "RoomRoles",
                column: "RoomId");

            migrationBuilder.CreateIndex(
                name: "IX_RoomRoles_RoomId_PlayerId_Role",
                table: "RoomRoles",
                columns: new[] { "RoomId", "PlayerId", "Role" },
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

            migrationBuilder.CreateIndex(
                name: "IX_RoomScenes_RoomId_Name",
                table: "RoomScenes",
                columns: new[] { "RoomId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_RoomScenes_RoomId_OrderIndex",
                table: "RoomScenes",
                columns: new[] { "RoomId", "OrderIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RoomVisits_PlayerId",
                table: "RoomVisits",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_RoomVisits_RoomId_LastVisitAt",
                table: "RoomVisits",
                columns: new[] { "RoomId", "LastVisitAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RoomVisits_RoomId_PlayerId",
                table: "RoomVisits",
                columns: new[] { "RoomId", "PlayerId" },
                unique: true);

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

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_SubscriberPlayerId",
                table: "Subscriptions",
                column: "SubscriberPlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_SubscriberPlayerId_TargetPlayerId",
                table: "Subscriptions",
                columns: new[] { "SubscriberPlayerId", "TargetPlayerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_TargetPlayerId",
                table: "Subscriptions",
                column: "TargetPlayerId");

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
                name: "AdminActions");

            migrationBuilder.DropTable(
                name: "Avatars");

            migrationBuilder.DropTable(
                name: "BugReports");

            migrationBuilder.DropTable(
                name: "Cards");

            migrationBuilder.DropTable(
                name: "ChatMessages");

            migrationBuilder.DropTable(
                name: "ChatThreadMembers");

            migrationBuilder.DropTable(
                name: "ChatThreads");

            migrationBuilder.DropTable(
                name: "Cheers");

            migrationBuilder.DropTable(
                name: "ClubAnnouncementReads");

            migrationBuilder.DropTable(
                name: "ClubAnnouncements");

            migrationBuilder.DropTable(
                name: "ClubCategoryAssignments");

            migrationBuilder.DropTable(
                name: "ClubCategoryTags");

            migrationBuilder.DropTable(
                name: "ClubMemberships");

            migrationBuilder.DropTable(
                name: "Clubs");

            migrationBuilder.DropTable(
                name: "ClubSubscriptions");

            migrationBuilder.DropTable(
                name: "CohortAssignments");

            migrationBuilder.DropTable(
                name: "CommunityBoardRows");

            migrationBuilder.DropTable(
                name: "CouponRedemptions");

            migrationBuilder.DropTable(
                name: "Coupons");

            migrationBuilder.DropTable(
                name: "CurrencyBalances");

            migrationBuilder.DropTable(
                name: "DormStates");

            migrationBuilder.DropTable(
                name: "GameRewardSelections");

            migrationBuilder.DropTable(
                name: "GameSessions");

            migrationBuilder.DropTable(
                name: "GiftPackages");

            migrationBuilder.DropTable(
                name: "Inventions");

            migrationBuilder.DropTable(
                name: "InventionVersions");

            migrationBuilder.DropTable(
                name: "IpBans");

            migrationBuilder.DropTable(
                name: "LeaderboardChannelMeta");

            migrationBuilder.DropTable(
                name: "LeaderboardStats");

            migrationBuilder.DropTable(
                name: "LoadingScreenTips");

            migrationBuilder.DropTable(
                name: "Messages");

            migrationBuilder.DropTable(
                name: "NotificationPrefs");

            migrationBuilder.DropTable(
                name: "ObjectiveProgress");

            migrationBuilder.DropTable(
                name: "Photos");

            migrationBuilder.DropTable(
                name: "PlatformIgnores");

            migrationBuilder.DropTable(
                name: "PlayerDevices");

            migrationBuilder.DropTable(
                name: "PlayerElo");

            migrationBuilder.DropTable(
                name: "PlayerEventResponses");

            migrationBuilder.DropTable(
                name: "PlayerEvents");

            migrationBuilder.DropTable(
                name: "PlayerInventory");

            migrationBuilder.DropTable(
                name: "PlayerSettings");

            migrationBuilder.DropTable(
                name: "PlaylistInteractions");

            migrationBuilder.DropTable(
                name: "PlaylistRooms");

            migrationBuilder.DropTable(
                name: "Playlists");

            migrationBuilder.DropTable(
                name: "PrivateInstanceInvitees");

            migrationBuilder.DropTable(
                name: "PrivateInstances");

            migrationBuilder.DropTable(
                name: "PushTokens");

            migrationBuilder.DropTable(
                name: "Relationships");

            migrationBuilder.DropTable(
                name: "Reports");

            migrationBuilder.DropTable(
                name: "RoomBans");

            migrationBuilder.DropTable(
                name: "RoomBookmarks");

            migrationBuilder.DropTable(
                name: "RoomDataBlobs");

            migrationBuilder.DropTable(
                name: "RoomKeyPurchases");

            migrationBuilder.DropTable(
                name: "RoomKeys");

            migrationBuilder.DropTable(
                name: "RoomRoles");

            migrationBuilder.DropTable(
                name: "Rooms");

            migrationBuilder.DropTable(
                name: "RoomScenes");

            migrationBuilder.DropTable(
                name: "RoomVisits");

            migrationBuilder.DropTable(
                name: "RoyaleMatches");

            migrationBuilder.DropTable(
                name: "RoyaleMatchPlayers");

            migrationBuilder.DropTable(
                name: "RoyalePlayerProgress");

            migrationBuilder.DropTable(
                name: "ServerSettings");

            migrationBuilder.DropTable(
                name: "StoreItems");

            migrationBuilder.DropTable(
                name: "Subscriptions");

            migrationBuilder.DropTable(
                name: "TestCases");

            migrationBuilder.DropTable(
                name: "TestPasses");

            migrationBuilder.DropTable(
                name: "Players");
        }
    }
}
