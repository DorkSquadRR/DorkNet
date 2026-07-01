namespace DorkNet.Server.Data.Entities;

/// <summary>
/// One admin-authored 3D Charades word list. The March 2023 client
/// pulls a deck at card-box spawn time from
/// <c>api/activities/charades/v1/words/{source}</c>, where
/// <c>{source}</c> is one of three baked <c>CardBox.cardSource</c>
/// slots — <c>Charades</c>, <c>CharadesAprilFoolsDay</c>, or
/// <c>Icebreakers</c> (verified in the 2023.03.21 il2cpp dump:
/// <c>GEFMIBEPMKJ.EODJJDILECA</c>). The game itself only ever requests
/// those three ids.
///
/// To let admins keep an unlimited library and freely switch which list
/// each slot serves, every list is a row here (name + words), and the
/// live slot→list binding lives in
/// <see cref="ServerSettingsEntity.CharadesSlotBindingsJson"/>. This is
/// the same admin-editable-collection shape as
/// <see cref="LoadingScreenTipEntity"/>: a plain entity CRUD'd from the
/// SPA, created on existing DBs via the idempotent bootstrap (it
/// post-dates the consolidated Initial migration).
/// </summary>
public class CharadesWordListEntity
{
    public long Id { get; set; }

    /// <summary>Admin-facing display name, e.g. "Default", "Movie night",
    /// "April Fools". Not sent to the client.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Serialized <c>List&lt;CharadesWord&gt;</c> — each entry is
    /// <c>{ Text, Difficulty }</c>. <c>Text</c> becomes the wire
    /// <c>EN_US</c> field; <c>Difficulty</c> is the client
    /// <c>CNMMMNJJDMM</c> enum (0 easy, 1 hard, 10 very hard, 20
    /// icebreaker). Stored as JSON so a list can grow to hundreds of
    /// cards without a child table.</summary>
    public string WordsJson { get; set; } = "[]";

    /// <summary>True for the lists seeded on first boot (Default /
    /// April Fools / Icebreakers). Purely informational — admins may
    /// still rename, edit, or delete them; it only gates the one-time
    /// seed so we don't duplicate them every startup.</summary>
    public bool IsBuiltIn { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
