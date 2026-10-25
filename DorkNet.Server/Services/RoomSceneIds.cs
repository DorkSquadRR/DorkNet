namespace DorkNet.Server.Services;

/// <summary>
/// Converts between a sub-room's position in the room
/// (<see cref="Data.Entities.RoomSceneEntity.OrderIndex"/>, 0-based, what the
/// server stores and indexes on) and the <c>SubRoomId</c> the 2023 client
/// sees on the wire.
///
/// <para><b>Why they differ.</b> The client refuses to load a sub-room whose
/// id is not positive: <c>SubroomModel.DOKCPLJOKIE</c>
/// (IsilDump/Assembly-CSharp/RRUI/Data/SubroomModel.txt:3) is
/// <c>RoomId &gt; 0 &amp;&amp; SubroomId &gt; 0</c>, and
/// <c>Model`1.LNPEMMMGCPC</c> skips the load when it is false. With the
/// first sub-room advertised as id 0, its Room Settings &gt; Subrooms row
/// never fetched its details (<c>SubroomModel.KBOJBJNCOGK</c> →
/// <c>FGCPNAACHIK.OBFPEGLLELP(long?)</c>), so the row showed no name, and
/// every per-row action (rename, max players, permissions, duplicate) ran
/// against a null sub-room. Rec Room's own ids were large unique numbers;
/// the client needs only "positive and stable per room".</para>
///
/// <para><b>Mapping.</b> Wire id = OrderIndex + 1, so a room's first
/// sub-room is 1. Incoming ids go back through <see cref="ToOrderIndex"/>;
/// 0 (and anything negative) is accepted as an alias of the first sub-room,
/// which keeps cached client state, old presence rows and bare
/// <c>matchmake/room/{id}</c> joins working.</para>
///
/// <para><b>Where the conversion happens.</b> Only at the wire: the room
/// details <c>SubRooms[]</c>, the sub-room routes
/// (<c>rooms/{id}/subrooms/{sub}/*</c>), <c>matchmake/room/{id}/{sub}</c>,
/// the room-instance JSON (<c>RoomInstanceDto.subRoomId</c>), the instance
/// browser, sub-room save DTOs and permission rows. Everything stored
/// (RoomScenes, PrivateInstances, RoomDataBlobs, presence) keeps the
/// OrderIndex.</para>
/// </summary>
public static class RoomSceneIds
{
    /// <summary>OrderIndex → wire SubRoomId (always ≥ 1).</summary>
    public static long ToWire(long orderIndex) => Math.Max(0, orderIndex) + 1;

    /// <summary>Wire SubRoomId → OrderIndex. 0 and negatives map to the first
    /// sub-room.</summary>
    public static int ToOrderIndex(long wireSubRoomId)
        => wireSubRoomId <= 1 ? 0 : (int)Math.Min(int.MaxValue, wireSubRoomId - 1);
}
