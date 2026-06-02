using Microsoft.EntityFrameworkCore;
using DorkNet.Server.Data.Entities;

namespace DorkNet.Server.Data;

public class DorkNetDbContext(DbContextOptions<DorkNetDbContext> options) : DbContext(options)
{
    public DbSet<PlayerEntity> Players => Set<PlayerEntity>();
    public DbSet<AvatarEntity> Avatars => Set<AvatarEntity>();
    public DbSet<PlayerSettingEntity> PlayerSettings => Set<PlayerSettingEntity>();
    public DbSet<RelationshipEntity> Relationships => Set<RelationshipEntity>();
    public DbSet<RoomEntity> Rooms => Set<RoomEntity>();
    public DbSet<RoomRoleEntity> RoomRoles => Set<RoomRoleEntity>();
    public DbSet<LeaderboardChannelMetaEntity> LeaderboardChannelMeta => Set<LeaderboardChannelMetaEntity>();
    public DbSet<RoomBookmarkEntity> RoomBookmarks => Set<RoomBookmarkEntity>();
    public DbSet<RoomDataBlobEntity> RoomDataBlobs => Set<RoomDataBlobEntity>();
    public DbSet<DormStateEntity> DormStates => Set<DormStateEntity>();
    public DbSet<AdminActionEntity> AdminActions => Set<AdminActionEntity>();
    public DbSet<InventionEntity> Inventions => Set<InventionEntity>();
    public DbSet<ReportEntity> Reports => Set<ReportEntity>();
    public DbSet<IpBanEntity> IpBans => Set<IpBanEntity>();
    public DbSet<MessageEntity> Messages => Set<MessageEntity>();
    public DbSet<ChatMessageEntity> ChatMessages => Set<ChatMessageEntity>();
    public DbSet<ChatThreadEntity> ChatThreads => Set<ChatThreadEntity>();
    public DbSet<ChatThreadMemberEntity> ChatThreadMembers => Set<ChatThreadMemberEntity>();
    public DbSet<CheerEntity> Cheers => Set<CheerEntity>();
    public DbSet<CurrencyBalanceEntity> CurrencyBalances => Set<CurrencyBalanceEntity>();
    public DbSet<PlayerEventEntity> PlayerEvents => Set<PlayerEventEntity>();
    public DbSet<PlayerEventResponseEntity> PlayerEventResponses => Set<PlayerEventResponseEntity>();
    public DbSet<ObjectiveProgressEntity> ObjectiveProgress => Set<ObjectiveProgressEntity>();
    public DbSet<SubscriptionEntity> Subscriptions => Set<SubscriptionEntity>();
    public DbSet<PhotoEntity> Photos => Set<PhotoEntity>();
    public DbSet<StoreItemEntity> StoreItems => Set<StoreItemEntity>();
    public DbSet<InventionVersionEntity> InventionVersions => Set<InventionVersionEntity>();
    public DbSet<RoomBanEntity> RoomBans => Set<RoomBanEntity>();
    public DbSet<PlayerInventoryEntity> PlayerInventory => Set<PlayerInventoryEntity>();
    public DbSet<GiftPackageEntity> GiftPackages => Set<GiftPackageEntity>();
    public DbSet<BugReportEntity> BugReports => Set<BugReportEntity>();
    public DbSet<ClubEntity> Clubs => Set<ClubEntity>();
    public DbSet<ClubMembershipEntity> ClubMemberships => Set<ClubMembershipEntity>();
    public DbSet<RoyaleMatchEntity> RoyaleMatches => Set<RoyaleMatchEntity>();
    public DbSet<RoyaleMatchPlayerEntity> RoyaleMatchPlayers => Set<RoyaleMatchPlayerEntity>();
    public DbSet<RoyalePlayerProgressEntity> RoyalePlayerProgress => Set<RoyalePlayerProgressEntity>();
    public DbSet<TestCaseEntity> TestCases => Set<TestCaseEntity>();
    public DbSet<TestPassEntity> TestPasses => Set<TestPassEntity>();
    public DbSet<PlayerDeviceEntity> PlayerDevices => Set<PlayerDeviceEntity>();
    public DbSet<NotificationPrefsEntity> NotificationPrefs => Set<NotificationPrefsEntity>();
    public DbSet<CohortAssignmentEntity> CohortAssignments => Set<CohortAssignmentEntity>();
    public DbSet<CouponEntity> Coupons => Set<CouponEntity>();
    public DbSet<CouponRedemptionEntity> CouponRedemptions => Set<CouponRedemptionEntity>();
    public DbSet<LeaderboardStatEntity> LeaderboardStats => Set<LeaderboardStatEntity>();
    public DbSet<PlayerEloEntity> PlayerElo => Set<PlayerEloEntity>();
    public DbSet<PushTokenEntity> PushTokens => Set<PushTokenEntity>();
    public DbSet<PlatformIgnoreEntity> PlatformIgnores => Set<PlatformIgnoreEntity>();
    public DbSet<CardEntity> Cards => Set<CardEntity>();
    public DbSet<RoomVisitEntity> RoomVisits => Set<RoomVisitEntity>();
    public DbSet<RoomSceneEntity> RoomScenes => Set<RoomSceneEntity>();
    public DbSet<PrivateInstanceEntity> PrivateInstances => Set<PrivateInstanceEntity>();
    public DbSet<PrivateInstanceInviteeEntity> PrivateInstanceInvitees => Set<PrivateInstanceInviteeEntity>();
    public DbSet<GameSessionEntity> GameSessions => Set<GameSessionEntity>();
    public DbSet<CommunityBoardEntity> CommunityBoardRows => Set<CommunityBoardEntity>();
    public DbSet<LoadingScreenTipEntity> LoadingScreenTips => Set<LoadingScreenTipEntity>();
    public DbSet<ServerSettingsEntity> ServerSettings => Set<ServerSettingsEntity>();
    public DbSet<RoomKeyEntity> RoomKeys => Set<RoomKeyEntity>();
    public DbSet<RoomKeyPurchaseEntity> RoomKeyPurchases => Set<RoomKeyPurchaseEntity>();
    public DbSet<GameRewardSelectionEntity> GameRewardSelections => Set<GameRewardSelectionEntity>();
    public DbSet<PlaylistEntity> Playlists => Set<PlaylistEntity>();
    public DbSet<PlaylistRoomEntity> PlaylistRooms => Set<PlaylistRoomEntity>();
    public DbSet<PlaylistInteractionEntity> PlaylistInteractions => Set<PlaylistInteractionEntity>();
    public DbSet<ClubAnnouncementEntity> ClubAnnouncements => Set<ClubAnnouncementEntity>();
    public DbSet<ClubAnnouncementReadEntity> ClubAnnouncementReads => Set<ClubAnnouncementReadEntity>();
    public DbSet<ClubCategoryTagEntity> ClubCategoryTags => Set<ClubCategoryTagEntity>();
    public DbSet<ClubCategoryAssignmentEntity> ClubCategoryAssignments => Set<ClubCategoryAssignmentEntity>();
    public DbSet<ClubSubscriptionEntity> ClubSubscriptions => Set<ClubSubscriptionEntity>();
    public DbSet<SignupCodeEntity> SignupCodes => Set<SignupCodeEntity>();
    public DbSet<PendingDeviceEntity> PendingDevices => Set<PendingDeviceEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // SQLite-specific NOCASE collation works via UseCollation; Postgres
        // doesn't have NOCASE. Provider-conditional helper applied to the
        // three string columns the wire API matches case-insensitively
        // (Room.Name, RoomScene.Name, Club.Name).
        //
        // FOLLOW-UP: on Postgres we currently fall back to default case-
        // sensitive collation on these columns. In practice this is fine
        // because the wire API users always type the same casing as the
        // stored value, but if we want strict parity with SQLite NOCASE
        // semantics we should add a functional LOWER(...) unique index
        // via raw SQL in a Postgres migration, OR enable the citext
        // extension and switch the column type. Tracked for post-cutover
        // hardening; not a launch blocker.
        var isSqlite = Database.IsSqlite();
        modelBuilder.Entity<PlayerEntity>(e =>
        {
            e.HasKey(p => p.Id);
            e.HasIndex(p => p.Username).IsUnique();
            e.HasOne(p => p.Avatar).WithOne(a => a.Player).HasForeignKey<AvatarEntity>(a => a.PlayerId);
            e.HasMany(p => p.Settings).WithOne(s => s.Player).HasForeignKey(s => s.PlayerId);
            e.HasMany(p => p.Relationships).WithOne(r => r.Requester).HasForeignKey(r => r.RequesterId);
        });

        modelBuilder.Entity<RelationshipEntity>(e =>
        {
            e.HasKey(r => r.Id);
            e.HasIndex(r => new { r.RequesterId, r.TargetId }).IsUnique();
        });

        modelBuilder.Entity<PlayerSettingEntity>(e =>
        {
            e.HasKey(s => s.Id);
            e.HasIndex(s => new { s.PlayerId, s.Key }).IsUnique();
        });

        modelBuilder.Entity<RoomEntity>(e =>
        {
            e.HasKey(r => r.Id);
            // Room names are case-insensitive in the wire API
            // (api/rooms/v2/name/{name}) — store collation-insensitive so
            // GetByName lookups don't depend on the user's casing.
            e.HasIndex(r => r.Name).IsUnique();
            if (isSqlite) e.Property(r => r.Name).UseCollation("NOCASE");
            // Hot-feed sorts by HotScore desc — keep it indexed.
            e.HasIndex(r => r.HotScore);
            e.HasIndex(r => r.CreatorPlayerId);
        });

        modelBuilder.Entity<RoomBookmarkEntity>(e =>
        {
            e.HasKey(b => b.Id);
            e.HasIndex(b => new { b.PlayerId, b.RoomId }).IsUnique();
            e.HasIndex(b => b.PlayerId);
        });

        modelBuilder.Entity<RoomDataBlobEntity>(e =>
        {
            e.HasKey(b => b.Id);
            // BlobName is the cdn url segment the client requests; must
            // be unique across the table because the catch-all matches
            // by string.
            e.HasIndex(b => b.BlobName).IsUnique();
            // "All versions of this room" + "find current" both index
            // by RoomId. Sort by UploadedAt desc when listing history.
            e.HasIndex(b => b.RoomId);
        });

        modelBuilder.Entity<RoomSceneEntity>(e =>
        {
            e.HasKey(s => s.Id);
            // BuildRoomDetails fetches all scenes for one room ordered
            // by OrderIndex. Composite index covers both predicate and
            // sort.
            e.HasIndex(s => new { s.RoomId, s.OrderIndex }).IsUnique();
            // Sub-room goto resolves by (room, name). Case-insensitive
            // match — same NOCASE collation pattern as RoomEntity.Name.
            e.HasIndex(s => new { s.RoomId, s.Name });
            if (isSqlite) e.Property(s => s.Name).UseCollation("NOCASE");
        });

        modelBuilder.Entity<DormStateEntity>(e =>
        {
            // PlayerId is already declared as [Key] on the entity; explicit
            // here so EF doesn't expect an Id auto-pk.
            e.HasKey(d => d.PlayerId);
        });

        modelBuilder.Entity<AdminActionEntity>(e =>
        {
            e.HasKey(a => a.Id);
            // The audit log is read in reverse-chronological order;
            // index Timestamp desc for the common "give me recent
            // admin actions" query.
            e.HasIndex(a => a.Timestamp);
            // Filter "all actions taken by admin X" or "all actions
            // targeting player Y" both want indexes.
            e.HasIndex(a => a.AdminPlayerId);
            e.HasIndex(a => new { a.TargetType, a.TargetId });
        });

        modelBuilder.Entity<InventionEntity>(e =>
        {
            e.HasKey(i => i.Id);
            e.HasIndex(i => i.CreatorPlayerId);
            // Popular tab orders by CheerCount desc; index it.
            e.HasIndex(i => i.CheerCount);
        });

        modelBuilder.Entity<ReportEntity>(e =>
        {
            e.HasKey(r => r.Id);
            // Admin queue reads "open reports ordered by oldest" —
            // index ResolvedAt so the WHERE IS NULL filter is fast.
            e.HasIndex(r => r.ResolvedAt);
            e.HasIndex(r => r.TargetPlayerId);
            e.HasIndex(r => r.ReporterPlayerId);
        });

        modelBuilder.Entity<IpBanEntity>(e =>
        {
            e.HasKey(b => b.Id);
            // Middleware queries by Cidr per request — keep it
            // indexed even though the table will be small.
            e.HasIndex(b => b.Cidr);
        });

        modelBuilder.Entity<MessageEntity>(e =>
        {
            e.HasKey(m => m.Id);
            // Inbox query: "messages I received, ordered by SentAt
            // desc". Index Recipient + SentAt for that path.
            e.HasIndex(m => new { m.RecipientPlayerId, m.SentAt });
            e.HasIndex(m => new { m.SenderPlayerId, m.SentAt });
        });

        modelBuilder.Entity<ChatMessageEntity>(e =>
        {
            e.HasKey(c => c.Id);
            // Thread fetch is "ThreadKey + SentAt desc, take 50".
            e.HasIndex(c => new { c.ThreadKey, c.SentAt });
        });

        modelBuilder.Entity<ChatThreadEntity>(e =>
        {
            e.HasKey(t => t.Id);
            // One metadata row per thread — concurrent rename calls
            // would otherwise duplicate-insert.
            e.HasIndex(t => t.ThreadKey).IsUnique();
        });

        modelBuilder.Entity<ChatThreadMemberEntity>(e =>
        {
            e.HasKey(m => m.Id);
            // (Thread, Player) is the natural key — Snooze/MarkRead/
            // Leave race conditions would otherwise insert duplicate
            // membership rows under the same player.
            e.HasIndex(m => new { m.ThreadKey, m.PlayerId }).IsUnique();
            e.HasIndex(m => m.PlayerId);
        });

        modelBuilder.Entity<CheerEntity>(e =>
        {
            e.HasKey(c => c.Id);
            // Re-cheer is idempotent on the (from, target, type)
            // tuple — unique index lets us upsert cleanly.
            e.HasIndex(c => new { c.FromPlayerId, c.TargetPlayerId, c.TargetRoomId, c.TargetPhotoId, c.Type })
             .IsUnique();
            e.HasIndex(c => c.TargetPlayerId);
            e.HasIndex(c => c.TargetRoomId);
            e.HasIndex(c => c.TargetPhotoId);
        });

        modelBuilder.Entity<PhotoEntity>(e =>
        {
            e.HasKey(p => p.Id);
            // Feed pagination is "WHERE IsPublic AND DeletedAt IS NULL
            // ORDER BY CreatedAt DESC" — index CreatedAt for that.
            e.HasIndex(p => p.CreatedAt);
            e.HasIndex(p => p.UploaderPlayerId);
            e.HasIndex(p => p.RoomId);
            // BlobName is the cdn URL the client requests; lookups by
            // it are common (admin moderation, dedupe checks).
            e.HasIndex(p => p.BlobName);
        });

        modelBuilder.Entity<StoreItemEntity>(e =>
        {
            e.HasKey(s => s.Id);
            // Slug is referenced from Avatar.InventoryJson — must be
            // unique so the inventory resolver gets a deterministic
            // hit when looking up "what does this slug mean?".
            e.HasIndex(s => s.Slug).IsUnique();
            // Storefront tab queries filter by storefront + active +
            // category in that order; index storefront for the most
            // selective predicate first.
            e.HasIndex(s => s.Storefront);
            e.HasIndex(s => new { s.IsActive, s.Category });
        });

        modelBuilder.Entity<CurrencyBalanceEntity>(e =>
        {
            e.HasKey(c => c.Id);
            // One wallet row per (player, currency) — unique on the
            // pair so AwardCurrency upserts cleanly.
            e.HasIndex(c => new { c.PlayerId, c.CurrencyType }).IsUnique();
        });

        modelBuilder.Entity<PlayerEventEntity>(e =>
        {
            e.HasKey(ev => ev.Id);
            e.HasIndex(ev => ev.CreatorPlayerId);
            e.HasIndex(ev => ev.StartsAt);
        });

        modelBuilder.Entity<PlayerEventResponseEntity>(e =>
        {
            e.HasKey(r => r.Id);
            // One response per (event, player); upsert via this
            // unique index.
            e.HasIndex(r => new { r.EventId, r.PlayerId }).IsUnique();
            e.HasIndex(r => r.PlayerId);
        });

        modelBuilder.Entity<ObjectiveProgressEntity>(e =>
        {
            e.HasKey(o => o.Id);
            e.HasIndex(o => new { o.PlayerId, o.Key }).IsUnique();
        });

        modelBuilder.Entity<SubscriptionEntity>(e =>
        {
            e.HasKey(s => s.Id);
            // One subscription per (subscriber, target) pair.
            e.HasIndex(s => new { s.SubscriberPlayerId, s.TargetPlayerId })
             .IsUnique();
            // SubscriberCount aggregations index by target.
            e.HasIndex(s => s.TargetPlayerId);
            e.HasIndex(s => s.SubscriberPlayerId);
        });

        modelBuilder.Entity<InventionVersionEntity>(e =>
        {
            e.HasKey(v => v.Id);
            // Version-list query: WHERE InventionId = X ORDER BY
            // VersionNumber DESC. Index covers both predicates.
            e.HasIndex(v => new { v.InventionId, v.VersionNumber })
             .IsUnique();
        });

        modelBuilder.Entity<RoomBanEntity>(e =>
        {
            e.HasKey(b => b.Id);
            // Match-service join check: "is this player banned from
            // this room?". Compound index makes that O(log n).
            e.HasIndex(b => new { b.RoomId, b.BannedPlayerId });
            e.HasIndex(b => b.BannedPlayerId);
        });

        modelBuilder.Entity<PlayerInventoryEntity>(e =>
        {
            e.HasKey(p => p.Id);
            // One row per (player, item) pair.
            e.HasIndex(p => new { p.PlayerId, p.ItemSlug }).IsUnique();
            e.HasIndex(p => p.PlayerId);
        });

        modelBuilder.Entity<GiftPackageEntity>(e =>
        {
            e.HasKey(g => g.Id);
            // Inbox query: unconsumed gifts for a player.
            e.HasIndex(g => new { g.RecipientPlayerId, g.Consumed });
        });

        modelBuilder.Entity<BugReportEntity>(e =>
        {
            e.HasKey(b => b.Id);
            // Admin queue reads "unread bugs ordered by oldest" —
            // ReadAt index for the WHERE IS NULL filter.
            e.HasIndex(b => b.ReadAt);
            e.HasIndex(b => b.ReporterPlayerId);
        });

        modelBuilder.Entity<ClubEntity>(e =>
        {
            e.HasKey(c => c.Id);
            e.HasIndex(c => c.Name).IsUnique();
            // GetByName lookups are case-insensitive on the wire.
            if (isSqlite) e.Property(c => c.Name).UseCollation("NOCASE");
            e.HasIndex(c => c.CreatorPlayerId);
        });

        modelBuilder.Entity<ClubMembershipEntity>(e =>
        {
            e.HasKey(m => m.Id);
            // One membership per (club, player) pair.
            e.HasIndex(m => new { m.ClubId, m.PlayerId }).IsUnique();
            e.HasIndex(m => m.PlayerId);
        });

        modelBuilder.Entity<ClubAnnouncementEntity>(e =>
        {
            e.HasKey(a => a.Id);
            // Per-club timeline query (newest first) — composite index
            // covers both predicate and sort.
            e.HasIndex(a => new { a.ClubId, a.CreatedAt });
        });

        modelBuilder.Entity<ClubAnnouncementReadEntity>(e =>
        {
            e.HasKey(r => r.Id);
            // One read-marker per (announcement, player) — upsert via
            // this unique index so re-marking is a no-op.
            e.HasIndex(r => new { r.AnnouncementId, r.PlayerId }).IsUnique();
            // The unread feeds query "what have I read?" by player id.
            e.HasIndex(r => r.PlayerId);
        });

        modelBuilder.Entity<ClubCategoryTagEntity>(e =>
        {
            e.HasKey(t => t.Id);
            // Tag names are case-insensitive in admin tooling; not
            // unique because admins may temporarily reuse a soft-
            // deleted name when iterating.
            e.HasIndex(t => t.Name);
        });

        modelBuilder.Entity<ClubCategoryAssignmentEntity>(e =>
        {
            e.HasKey(a => a.Id);
            // One assignment per (club, tag) pair — re-assigning the
            // same tag should be idempotent.
            e.HasIndex(a => new { a.ClubId, a.CategoryTagId }).IsUnique();
            e.HasIndex(a => a.CategoryTagId);
        });

        modelBuilder.Entity<ClubSubscriptionEntity>(e =>
        {
            e.HasKey(s => s.Id);
            // One subscription row per (player, club). Unique so the
            // subscribe-then-resubscribe path can upsert cleanly.
            e.HasIndex(s => new { s.PlayerId, s.ClubId }).IsUnique();
            e.HasIndex(s => s.ClubId);
        });

        modelBuilder.Entity<RoyaleMatchEntity>(e =>
        {
            e.HasKey(m => m.Id);
            e.HasIndex(m => m.CompletedAt);
        });

        modelBuilder.Entity<RoyaleMatchPlayerEntity>(e =>
        {
            e.HasKey(p => p.Id);
            e.HasIndex(p => new { p.MatchId, p.PlayerId });
            e.HasIndex(p => p.PlayerId);
        });

        modelBuilder.Entity<RoyalePlayerProgressEntity>(e =>
        {
            e.HasKey(p => p.Id);
            e.HasIndex(p => p.PlayerId).IsUnique();
        });

        modelBuilder.Entity<TestCaseEntity>(e =>
        {
            e.HasKey(t => t.Pk);
            // Wire Id (string) is what the client uses in URL params;
            // unique so the lookup-by-string-id is fast.
            e.HasIndex(t => t.Id).IsUnique();
            e.HasIndex(t => t.TestPassId);
            e.HasIndex(t => t.Status);
        });

        modelBuilder.Entity<TestPassEntity>(e =>
        {
            e.HasKey(p => p.Id);
            // Don't auto-generate uint Id; we set it explicitly so it
            // matches the upstream JIRA pass id when seeded.
            e.Property(p => p.Id).ValueGeneratedNever();
        });

        modelBuilder.Entity<PlayerDeviceEntity>(e =>
        {
            e.HasKey(d => d.Id);
            // Lookup "all accounts using this device" for ban
            // enforcement.
            e.HasIndex(d => d.DeviceId);
            e.HasIndex(d => d.PlayerId);
        });

        modelBuilder.Entity<NotificationPrefsEntity>(e =>
        {
            e.HasKey(n => n.Id);
            // One pref row per (player, platform).
            e.HasIndex(n => new { n.PlayerId, n.Platform }).IsUnique();
        });

        modelBuilder.Entity<CohortAssignmentEntity>(e =>
        {
            e.HasKey(c => c.Id);
            // Sticky assignment: one per (player, cohort).
            e.HasIndex(c => new { c.PlayerId, c.CohortKey }).IsUnique();
        });

        modelBuilder.Entity<CouponEntity>(e =>
        {
            e.HasKey(c => c.Id);
            e.HasIndex(c => c.Code).IsUnique();
        });

        modelBuilder.Entity<CouponRedemptionEntity>(e =>
        {
            e.HasKey(r => r.Id);
            // Enforce one-redeem-per-player.
            e.HasIndex(r => new { r.CouponId, r.PlayerId }).IsUnique();
        });

        modelBuilder.Entity<LeaderboardStatEntity>(e =>
        {
            e.HasKey(s => s.Id);
            // One row per (player, channel) — enforced unique so
            // SetStats can upsert without race conditions.
            e.HasIndex(s => new { s.PlayerId, s.StatChannel }).IsUnique();
            // Rank computation: ORDER BY Value DESC over a channel.
            e.HasIndex(s => new { s.StatChannel, s.Value });
        });

        modelBuilder.Entity<PlayerEloEntity>(e =>
        {
            e.HasKey(p => p.Id);
            e.HasIndex(p => new { p.PlayerId, p.GameMode }).IsUnique();
            // Matchmaker reads ranked players ordered by Elo DESC.
            e.HasIndex(p => new { p.GameMode, p.Elo });
        });

        modelBuilder.Entity<PushTokenEntity>(e =>
        {
            e.HasKey(t => t.Id);
            // One token per (player, platform) — APNS revokes the
            // old one when the user reinstalls, so we upsert.
            e.HasIndex(t => new { t.PlayerId, t.Platform }).IsUnique();
            e.HasIndex(t => t.Token);
        });

        modelBuilder.Entity<PlatformIgnoreEntity>(e =>
        {
            e.HasKey(i => i.Id);
            // One ignore-row per (player, platform, platform user id).
            e.HasIndex(i => new { i.PlayerId, i.Platform, i.PlatformUserId }).IsUnique();
        });

        modelBuilder.Entity<CardEntity>(e =>
        {
            e.HasKey(c => c.Id);
            // Home-screen query: WHERE PlayerId IS NULL OR = me
            // ORDER BY Priority DESC, CreatedAt DESC.
            e.HasIndex(c => c.PlayerId);
            e.HasIndex(c => c.ExpiresAt);
        });

        modelBuilder.Entity<RoomVisitEntity>(e =>
        {
            e.HasKey(v => v.Id);
            // One row per (room, player) — enforced unique so the
            // visit-upsert doesn't create duplicates under concurrent
            // /goto calls. Lookup-by-pair is the hot path so the
            // composite index doubles as the SELECT support index.
            e.HasIndex(v => new { v.RoomId, v.PlayerId }).IsUnique();
            // Per-player history view (admin / dev console).
            e.HasIndex(v => v.PlayerId);
            // "Recent visitors of this room" view.
            e.HasIndex(v => new { v.RoomId, v.LastVisitAt });
        });

        modelBuilder.Entity<RoomRoleEntity>(e =>
        {
            e.HasKey(r => r.Id);
            e.HasIndex(r => r.RoomId);
            e.HasIndex(r => new { r.RoomId, r.PlayerId, r.Role }).IsUnique();
        });

        modelBuilder.Entity<PrivateInstanceEntity>(e =>
        {
            // Id is server-allocated (computed nonce), not autoincrement.
            // Mark ValueGenerated.Never so EF doesn't try to round-trip
            // through a sequence on insert.
            e.HasKey(p => p.Id);
            e.Property(p => p.Id).ValueGeneratedNever();
            e.HasIndex(p => p.OwnerPlayerId);
            // Quick lookup for "is this player in any private instance"
            // queries used by NotificationService.
            e.HasIndex(p => new { p.RoomId, p.SubRoomId });
        });

        modelBuilder.Entity<PrivateInstanceInviteeEntity>(e =>
        {
            // Composite PK: one row per (instance, invitee). Naturally
            // dedupes repeat-invite calls.
            e.HasKey(i => new { i.PrivateInstanceId, i.PlayerId });
            // Reverse-lookup index for "what private instances am I
            // invited to" queries (the watch's invite inbox).
            e.HasIndex(i => i.PlayerId);
        });

        modelBuilder.Entity<GameSessionEntity>(e =>
        {
            // Postgres bigserial; SQLite gets INTEGER PRIMARY KEY
            // AUTOINCREMENT semantics. Both providers' sequences are
            // process-shared so two replicas don't allocate id=1.
            e.HasKey(s => s.Id);
            // The hot lookup is "find a non-full session in this room
            // and region" — composite index covers it.
            e.HasIndex(s => new { s.RoomId, s.Region });
        });

        modelBuilder.Entity<CommunityBoardEntity>(e =>
        {
            // Single-row table, id always 1. We don't autoincrement;
            // the row is upserted explicitly.
            e.HasKey(c => c.Id);
            e.Property(c => c.Id).ValueGeneratedNever();
        });

        modelBuilder.Entity<ServerSettingsEntity>(e =>
        {
            // Single-row table, id always 1 — same pattern as
            // CommunityBoardRows above.
            e.HasKey(s => s.Id);
            e.Property(s => s.Id).ValueGeneratedNever();
            e.Property(s => s.WeeklyChallengesCompletedRequired).HasDefaultValue(true);
            e.Property(s => s.WeeklyChallengesJson).HasDefaultValue(string.Empty);
            e.Property(s => s.WeeklyChallengeRewardJson).HasDefaultValue(string.Empty);
            e.Property(s => s.PlayMenuTagsJson).HasDefaultValue(string.Empty);
            e.Property(s => s.RecCenterDoorsJson).HasDefaultValue(string.Empty);
            e.Property(s => s.DiscoveredGameConfigsJson).HasDefaultValue(string.Empty);
        });

        modelBuilder.Entity<RoomKeyEntity>(e =>
        {
            e.HasKey(k => k.Id);
            e.HasIndex(k => k.RoomId);
            e.HasIndex(k => new { k.RoomId, k.Name });
            e.HasIndex(k => k.CreatorPlayerId);
        });

        modelBuilder.Entity<RoomKeyPurchaseEntity>(e =>
        {
            e.HasKey(p => p.Id);
            e.HasIndex(p => new { p.RoomKeyId, p.PlayerId }).IsUnique();
            e.HasIndex(p => p.PlayerId);
        });

        modelBuilder.Entity<GameRewardSelectionEntity>(e =>
        {
            e.HasKey(r => r.Id);
            e.HasIndex(r => new { r.PlayerId, r.SelectedAt });
        });

        modelBuilder.Entity<PlaylistEntity>(e =>
        {
            e.HasKey(p => p.Id);
            // Playlist names share the same case-insensitive lookup
            // contract as Room.Name — the watch's search compares
            // user-typed strings directly against the stored value.
            e.HasIndex(p => p.Name);
            if (isSqlite) e.Property(p => p.Name).UseCollation("NOCASE");
            // Curated tab reads "WHERE IsCurated ORDER BY OrderIndex" —
            // index covers the predicate and the sort key.
            e.HasIndex(p => p.IsCurated);
            e.HasIndex(p => p.CreatorPlayerId);
        });

        modelBuilder.Entity<PlaylistRoomEntity>(e =>
        {
            e.HasKey(pr => pr.Id);
            // Member-rooms query: "WHERE PlaylistId = X ORDER BY
            // OrderIndex". Composite covers both predicate and sort.
            e.HasIndex(pr => new { pr.PlaylistId, pr.OrderIndex });
            e.HasIndex(pr => pr.PlaylistId);
            e.HasIndex(pr => pr.RoomId);
        });

        modelBuilder.Entity<PlaylistInteractionEntity>(e =>
        {
            e.HasKey(i => i.Id);
            // One row per (playlist, player) — toggle endpoints upsert.
            e.HasIndex(i => new { i.PlaylistId, i.PlayerId }).IsUnique();
            // "cheeredby/me" / "favoritedby/me" lists query by PlayerId
            // + the bool flag; PlayerId-leading index is enough since
            // the table is small.
            e.HasIndex(i => i.PlayerId);
        });

        modelBuilder.Entity<LeaderboardChannelMetaEntity>(e =>
        {
            // Channel int IS the primary key — there's only one
            // metadata row per stat-channel id, and the channel id
            // is what the watch reports on POST /SetStats.
            e.HasKey(c => c.Channel);
            e.Property(c => c.Channel).ValueGeneratedNever();
            e.HasIndex(c => c.RoomId);
        });
    }
}
