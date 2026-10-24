using System.Text.Json;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace DorkNet.Server.Services;

/// <summary>
/// A sub-room's role-permission matrix: the cells the 2023 client reads from
/// <c>GET photon_access_token</c> (<c>ALIIIAGAPND.Permissions</c>, a list of
/// <c>BNLPBMJJOMM</c>) and writes with
/// <c>PUT rooms/{id}/subrooms/{sub}/permissions</c>.
///
/// <para>The served list is the built-in defaults with every stored cell for
/// the room's sub-room applied on top: a stored cell replaces the default
/// with the same (Role, Permission), and cells with no default are appended.
/// The list must never be empty — with no Permissions the room-permissions
/// runtime (OMJKHJLFOCO) falls into its persisted-role migration path, which
/// NREs during first spawn.</para>
///
/// <para>Cell keys (formatter RecNet.Runtime/HDOJMEBCAGP.txt):
/// <c>Permission</c> (string), <c>Role</c> (OMMBGJMJJPN), <c>Override</c>
/// (bool), <c>Type</c> (FEOJFGECKGH: Bool 0 / Int 1 / StringList 2),
/// <c>Value</c> (string). Role and Type go out as numbers; requests are
/// accepted with numbers, numeric strings or enum names.</para>
/// </summary>
public static class RoomPermissionMatrix
{
    /// <summary>One matrix cell.</summary>
    public sealed record Cell(string Permission, int Role, bool Override, int Type, string Value);

    /// <summary>The defaults every sub-room starts from: Co-Owner and Creator
    /// may use the maker pen, spawn inventions and consumables, reset the
    /// room and toggle play gizmos.</summary>
    public static IReadOnlyList<Cell> Defaults { get; } = BuildDefaults();

    private static IReadOnlyList<Cell> BuildDefaults()
    {
        var permissions = new[]
        {
            "13", // Can Use Maker Pen
            "20", // Can Spawn Inventions
            "21", // Can Spawn Consumables
            "22", // Can Use Room Reset Button
            "23", // Can Use Play Gizmos Toggle
        };
        var list = new List<Cell>();
        foreach (var role in new[] { RoomRoleRules.WireCoOwner, RoomRoleRules.WireCreator })
            foreach (var permission in permissions)
                list.Add(new Cell(permission, role, true, 0, "true"));
        return list;
    }

    /// <summary>Defaults merged with the stored cells.</summary>
    public static List<Cell> Merge(IEnumerable<RoomRolePermissionEntity> stored)
    {
        var cells = Defaults.ToList();
        foreach (var row in stored.OrderBy(r => r.Id))
        {
            var cell = new Cell(row.Permission, row.Role, row.Override, row.Type, row.Value);
            var at = cells.FindIndex(c =>
                c.Role == row.Role
                && string.Equals(c.Permission, row.Permission, StringComparison.OrdinalIgnoreCase));
            if (at >= 0) cells[at] = cell;
            else cells.Add(cell);
        }
        return cells;
    }

    /// <summary>The served matrix for one sub-room (by OrderIndex).</summary>
    public static async Task<List<Cell>> LoadAsync(DorkNetDbContext db, long roomId, long subRoomOrderIndex)
    {
        var stored = await db.RoomRolePermissions.AsNoTracking()
            .Where(p => p.RoomId == roomId && p.SubRoomId == subRoomOrderIndex)
            .ToListAsync();
        return Merge(stored);
    }

    /// <summary>The <c>ALIIIAGAPND</c> body: <c>{RoomInstanceId,
    /// PhotonAccessToken, Permissions}</c>. Used both by
    /// <c>GET photon_access_token</c> and the <c>PhotonAccessToken</c> hub
    /// push (IBEOONPEELF.txt:519 registers it as Action&lt;ALIIIAGAPND&gt;).</summary>
    public static Dictionary<string, object> AccessTokenBody(long roomInstanceId, IEnumerable<Cell> cells)
        => new()
        {
            ["RoomInstanceId"] = roomInstanceId,
            ["PhotonAccessToken"] = string.Empty,
            ["Permissions"] = cells.Select(ToWire).ToList(),
        };

    public static object ToWire(Cell c) => new
    {
        c.Permission,
        c.Role,
        c.Override,
        c.Type,
        c.Value,
    };

    /// <summary>Parse the PUT body: a JSON array of cells (an object wrapping
    /// a <c>Permissions</c> array is accepted too). Returns null when the body
    /// is not JSON or holds no array.</summary>
    public static List<Cell>? Parse(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        JsonDocument doc;
        try { doc = JsonDocument.Parse(body); }
        catch (JsonException) { return null; }
        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                root = root.EnumerateObject()
                    .Where(p => p.Name.Equals("Permissions", StringComparison.OrdinalIgnoreCase))
                    .Select(p => p.Value)
                    .FirstOrDefault();
            }
            if (root.ValueKind != JsonValueKind.Array) return null;

            var cells = new List<Cell>();
            foreach (var item in root.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                string? permission = null;
                int? role = null;
                bool @override = false;
                int type = 0;
                string value = string.Empty;
                foreach (var prop in item.EnumerateObject())
                {
                    switch (prop.Name.ToLowerInvariant())
                    {
                        case "permission":
                            permission = Scalar(prop.Value)?.Trim();
                            break;
                        case "role":
                            role = EnumValue(prop.Value, RoleName);
                            break;
                        case "type":
                            type = EnumValue(prop.Value, TypeName) ?? 0;
                            break;
                        case "override":
                            @override = prop.Value.ValueKind == JsonValueKind.True
                                || (prop.Value.ValueKind == JsonValueKind.String
                                    && bool.TryParse(prop.Value.GetString(), out var b) && b);
                            break;
                        case "value":
                            value = Scalar(prop.Value) ?? string.Empty;
                            break;
                    }
                }
                if (string.IsNullOrEmpty(permission) || role is null) continue;
                if (permission.Length > 128 || value.Length > 4096) continue;
                cells.Add(new Cell(permission, role.Value, @override, type, value));
            }
            return cells;
        }
    }

    private static string? Scalar(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => e.GetString(),
        JsonValueKind.Number => e.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        _ => e.GetRawText(),
    };

    private static int? EnumValue(JsonElement e, Func<string, int?> byName)
    {
        if (e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out var n)) return n;
        if (e.ValueKind != JsonValueKind.String) return null;
        var s = e.GetString() ?? string.Empty;
        return int.TryParse(s, out n) ? n : byName(s);
    }

    private static int? RoleName(string s) => s.Trim().ToLowerInvariant() switch
    {
        "none" => RoomRoleRules.WireNone,
        "banned" => RoomRoleRules.WireBanned,
        "host" => RoomRoleRules.WireHost,
        "moderator" => RoomRoleRules.WireModerator,
        "coowner" => RoomRoleRules.WireCoOwner,
        "temporarycoowner" => RoomRoleRules.WireTemporaryCoOwner,
        "creator" => RoomRoleRules.WireCreator,
        _ => null,
    };

    private static int? TypeName(string s) => s.Trim().ToLowerInvariant() switch
    {
        "bool" => 0,
        "int" => 1,
        "stringlist" => 2,
        _ => null,
    };
}
