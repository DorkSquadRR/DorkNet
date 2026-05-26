using System.ComponentModel.DataAnnotations;

namespace DorkNet.Server.Data.Entities;

/// <summary>
/// One scene within a multi-scene custom room. Most rooms historically
/// had only Scenes[0] (synthesised in <c>BuildRoomDetails</c> from
/// <see cref="RoomEntity.LocationReplicationId"/> +
/// <see cref="RoomEntity.CurrentDataBlobName"/>); rooms imported from a
/// multi-scene archive write one row per chapter here, and the wire-shape
/// builder emits the full <c>Scenes[]</c> array from this table when any
/// rows exist.
///
/// <para>The scene's <see cref="RoomSceneId"/> on the wire is this table's
/// <see cref="OrderIndex"/> — the matchmaking <c>SubRoomId</c> the watch
/// passes to <c>/goto/room/{name}/{subroom}</c> matches against
/// <see cref="Name"/>, then resolves to <see cref="OrderIndex"/> for
/// <c>RoomInstance.SubRoomId</c>.</para>
/// </summary>
public class RoomSceneEntity
{
    public long Id { get; set; }

    /// <summary>FK to <see cref="RoomEntity"/>. Indexed so the per-room
    /// fetch in <c>BuildRoomDetails</c> is one query.</summary>
    public long RoomId { get; set; }

    /// <summary>0-based index this scene occupies in the wire
    /// <c>Scenes[]</c> array. Doubles as the <c>RoomSceneId</c> the
    /// matchmaking flow uses for <c>RoomInstance.SubRoomId</c>.</summary>
    public int OrderIndex { get; set; }

    /// <summary>Display name (e.g. "Lobby", "Ch1_Headfirst"). Matched
    /// case-insensitively against the sub-room segment of
    /// <c>/goto/room/{name}/{subroom}</c>.</summary>
    [MaxLength(128)]
    public string Name { get; set; } = string.Empty;

    /// <summary>The <c>RoomSceneLocationId</c> GUID the client uses to
    /// look up the bundled stage prefab via
    /// <c>AGRoomSettings.TryGetRoomSceneLocationById</c>. Defaults to
    /// the MakerRoom (Basement) scene GUID — the canonical "blank
    /// canvas" stage 2020 custom rooms were built on. Extracted from
    /// resources.assets via tools/extract-locations-binary.py: the
    /// "Maker Room" Location entry has SceneName=Basement and
    /// ReplicationId=a75f7547-79eb-47c6-8986-6767abcb4f92.</summary>
    [MaxLength(64)]
    public string RoomSceneLocationId { get; set; } = "a75f7547-79eb-47c6-8986-6767abcb4f92";

    /// <summary>The <see cref="RoomDataBlobEntity.BlobName"/> the watch
    /// downloads as the persisted state for this scene.</summary>
    [MaxLength(128)]
    public string DataBlobName { get; set; } = string.Empty;

    public int MaxPlayers { get; set; } = 8;
    public bool IsSandbox { get; set; } = false;
    public bool CanMatchmakeInto { get; set; } = true;

    public DateTime DataModifiedAt { get; set; } = DateTime.UtcNow;
}
