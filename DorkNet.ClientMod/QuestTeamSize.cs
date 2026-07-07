// Raise RRO quest party size past the baked 4 — precisely, only for the
// allowlisted quests (Cfg.QuestMaxTeamSizeRooms).
//
// HOW THE CAP IS REALLY BUILT (cracked from the December dump/ISIL):
//   GameConfigurationTool.Awake()
//     → reads its baked `defaultGameConfiguration` (GameConfigurationAsset, the
//       Unity ScriptableObject @0x58)
//     → calls GameConfigurationAsset.GDECPANDBHJ()  ← builds the runtime
//       protobuf GameConfigurationData (PJPAPHKPKGM) FROM the asset
//     → stores it @0x88 and applies it (OAHJOLJOKGI) to the team manager.
//   Inside GDECPANDBHJ the build loop reads asset.TeamConfigurations[i]
//   .MaxTeamSize (a typed int struct-array: [arr+0x20 + i*4]) and writes each
//   straight into a new GameTeamConfigurationData.TeamSize (HFBCEJLLDLI @0x10).
//
// So the runtime cap originates from the asset's `TeamConfiguration[]` — a
// READABLE, typed struct array — NOT from anything that needs the obfuscated
// protobuf. Earlier attempts failed because:
//   • bumping the asset on a 2s poll happened AFTER GDECPANDBHJ already built &
//     cached the protobuf (timing, not the wrong field);
//   • patching the protobuf getters (get_FNJLAGHPMFL / get_FBOCAPLIIDH) is
//     impossible — Il2CppInterop doesn't emit the generic RepeatedField<T>
//     getter, and the simple int getter wasn't emitted either.
//
// THE FIX: Harmony-PREFIX GameConfigurationAsset.GDECPANDBHJ() and bump the
// asset's TeamConfigurations[i].MaxTeamSize to the target IN PLACE, right
// before it builds the protobuf. The built config then carries the new cap and
// the team manager / pre-game roster / open-slots all honour it. Scoped by
// asset.Name to the allowlist (PvP/sandbox configs are left alone).
//
// HOST-AUTHORITATIVE: the quest host builds the config it networks to joiners
// (RpcDeserializeConfig), so the host must run this mod for everyone to see the
// raised cap. (A joiner can't override — that would need the unreachable
// protobuf path.)
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace DorkNet.ClientMod;

internal static class QuestTeamSize
{
    private const BindingFlags BF = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    public static bool TryPatchSpawnIndexNormalizers(HarmonyLib.Harmony harmony)
    {
        var ok = true;
        ok &= TryPatchTeamIndexNormalizer(harmony);
        ok &= TryPatchSpawnModeDataNormalizer(harmony);
        ok &= TryPatchRequiredSpawnIndexNormalizer(harmony);
        return ok;
    }

    private static bool TryPatchTeamIndexNormalizer(HarmonyLib.Harmony harmony)
    {
        try
        {
            var managerType = Mod.ResolveType("GameTeamManager");
            var indexType = Mod.ResolveType("CCMLKDOHKJE");
            if (managerType is null || indexType is null)
            {
                Mod.Log.Warning("[patch-miss] GameTeamManager.GetTeamPlayerIndex: type not found");
                return false;
            }

            var target = AccessTools.Method(managerType, "GetTeamPlayerIndex");
            if (target is null)
            {
                Mod.Log.Warning("[patch-miss] GameTeamManager.GetTeamPlayerIndex");
                return false;
            }

            var patch = typeof(QuestTeamSize)
                .GetMethod(nameof(NormalizeTeamPlayerIndex_Postfix), BindingFlags.Public | BindingFlags.Static)!
                .MakeGenericMethod(indexType);
            harmony.Patch(target, postfix: new HarmonyMethod(patch));
            return true;
        }
        catch (Exception ex)
        {
            Mod.Log.Error($"[patch-fail] GameTeamManager.GetTeamPlayerIndex: {ex.Message}");
            return false;
        }
    }

    private static bool TryPatchSpawnModeDataNormalizer(HarmonyLib.Harmony harmony)
    {
        try
        {
            var spawnType = Mod.ResolveType("GameSpawnManager");
            var indexType = Mod.ResolveType("CCMLKDOHKJE");
            if (spawnType is null || indexType is null)
            {
                Mod.Log.Warning("[patch-miss] GameSpawnManager.ALCADGJKKHJ: type not found");
                return false;
            }

            var target = AccessTools.Method(spawnType, "ALCADGJKKHJ");
            if (target is null)
            {
                Mod.Log.Warning("[patch-miss] GameSpawnManager.ALCADGJKKHJ");
                return false;
            }

            var patch = typeof(QuestTeamSize)
                .GetMethod(nameof(NormalizeSpawnModeData_Postfix), BindingFlags.Public | BindingFlags.Static)!
                .MakeGenericMethod(indexType);
            harmony.Patch(target, postfix: new HarmonyMethod(patch));
            return true;
        }
        catch (Exception ex)
        {
            Mod.Log.Error($"[patch-fail] GameSpawnManager.ALCADGJKKHJ: {ex.Message}");
            return false;
        }
    }

    private static bool TryPatchRequiredSpawnIndexNormalizer(HarmonyLib.Harmony harmony)
    {
        try
        {
            var spawnType = Mod.ResolveType("GameSpawnManager");
            var indexType = Mod.ResolveType("CCMLKDOHKJE");
            if (spawnType is null || indexType is null)
            {
                Mod.Log.Warning("[patch-miss] GameSpawnManager.FKEICBPCMPJ index normalizer: type not found");
                return false;
            }

            var target = AccessTools.Method(spawnType, "FKEICBPCMPJ");
            if (target is null)
            {
                Mod.Log.Warning("[patch-miss] GameSpawnManager.FKEICBPCMPJ index normalizer");
                return false;
            }

            var patch = typeof(QuestTeamSize)
                .GetMethod(nameof(NormalizeRequiredSpawnIndex_Prefix), BindingFlags.Public | BindingFlags.Static)!
                .MakeGenericMethod(indexType);
            harmony.Patch(target, prefix: new HarmonyMethod(patch));
            return true;
        }
        catch (Exception ex)
        {
            Mod.Log.Error($"[patch-fail] GameSpawnManager.FKEICBPCMPJ index normalizer: {ex.Message}");
            return false;
        }
    }

    // POSTFIX on GameTeamManager.GetTeamPlayerIndex(Player). The RRO quest
    // scenes only have spawn points for indexes 0-3. After raising the quest
    // team cap, players 5+ legitimately receive INDEX_4+, but those values
    // cannot match any baked quest spawn point. Normalize just the enum result
    // that spawn code asks for; the actual team roster/cap remains raised.
    public static void NormalizeTeamPlayerIndex_Postfix<T>(ref T __result) where T : struct
    {
        try
        {
            if (Mod.Cfg.QuestMaxTeamSize <= 4) return;

            var value = Convert.ToInt32(__result);
            if (!TryNormalizeIndex(value, out var normalized)) return;

            __result = (T)Enum.ToObject(typeof(T), normalized);
        }
        catch (Exception ex)
        {
            Mod.Log.Warning($"[questsize] team-player index normalize failed: {ex.Message}");
        }
    }

    // POSTFIX on GameSpawnManager.ALCADGJKKHJ(..., out CCMLKDOHKJE index, ...).
    // FPBFJMMKPPJ calls this private helper immediately before FKEICBPCMPJ, so
    // this is the exact spawn-path value seen in the "TeamIndex INDEX_4" log.
    public static void NormalizeSpawnModeData_Postfix<T>(ref T __3) where T : struct
    {
        try
        {
            if (Mod.Cfg.QuestMaxTeamSize <= 4) return;

            var value = Convert.ToInt32(__3);
            if (!TryNormalizeIndex(value, out var normalized)) return;

            __3 = (T)Enum.ToObject(typeof(T), normalized);
        }
        catch (Exception ex)
        {
            Mod.Log.Warning($"[questsize] spawn-mode index normalize failed: {ex.Message}");
        }
    }

    // PREFIX on GameSpawnManager.FKEICBPCMPJ(..., CCMLKDOHKJE requiredIndex, ...).
    // This is the last guard before the spawn point field compare at +0x88.
    public static void NormalizeRequiredSpawnIndex_Prefix<T>(ref T __2) where T : struct
    {
        try
        {
            if (Mod.Cfg.QuestMaxTeamSize <= 4) return;

            var value = Convert.ToInt32(__2);
            if (!TryNormalizeIndex(value, out var normalized)) return;

            __2 = (T)Enum.ToObject(typeof(T), normalized);
        }
        catch (Exception ex)
        {
            Mod.Log.Warning($"[questsize] required spawn index normalize failed: {ex.Message}");
        }
    }

    private static bool TryNormalizeIndex(int value, out int normalized)
    {
        normalized = value % 4;
        return value >= 4;
    }

    // PREFIX on GameConfigurationAsset.GDECPANDBHJ()  (asset → protobuf builder).
    public static void Build_Prefix(object __instance)
    {
        try
        {
            var target = Mod.Cfg.QuestMaxTeamSize;
            if (target <= 0 || __instance is null) return;

            var name = GetMember(__instance, "Name") as string ?? "";
            if (!IsAllowed(name)) return;

            var arr = GetMember(__instance, "TeamConfigurations");
            int len = ArrLen(arr);
            if (len <= 0) return;

            int bumped = 0;
            for (int i = 0; i < len; i++)
            {
                var elem = ArrItem(arr, i);     // boxed TeamConfiguration struct (copy)
                if (elem is null) continue;
                int cur = ReadInt(elem, "MaxTeamSize");
                if (cur > 0 && cur < target &&
                    WriteInt(elem, "MaxTeamSize", target) &&
                    SetArrItem(arr!, i, elem))   // write the mutated struct back
                    bumped++;
            }
        }
        catch (Exception ex) { Mod.Log.Warning($"[questsize] build prefix failed: {ex.Message}"); }
    }

    // POSTFIX on GameSpawnManager.FKEICBPCMPJ(..., outputList). That method
    // filters spawn points by state/game-mode/spectator/team/team-player-index/
    // custom-tag before the spawn selector runs. The RRO quest prefabs only
    // have indexed start points for players 0-3; once we raise the team size,
    // player index 4+ can filter every point out before the built-in overlap
    // fallback has a chance to help.
    //
    // December decompile: FPBFJMMKPPJ calls this filter, then if output.Count
    // is still 0 it logs "due to spawn point requirement tests" and falls back
    // to the unfiltered source list as an arbitrary spawn set. Filling the
    // output list here keeps the normal overlap pass and spawn-selection mode
    // alive instead of taking that bad arbitrary fallback.
    //
    // Harmony binds __0 and __7 by argument position: source list and output
    // list. The obfuscated parameter names are not reliable in MelonLoader's
    // generated IL2CPP metadata, which is why this intentionally avoids them.
    // If the filtered output is empty, copy the already-selected candidate
    // scene spawn list back into the output so extra players reuse baked quest
    // spawn points instead of being left out in the void.
    public static void RelaxEmptySpawnFilter_Postfix(object __0, object __7)
    {
        try
        {
            if (Mod.Cfg.QuestMaxTeamSize <= 4) return;
            if (Count(__7) > 0) return;

            var sourceCount = Count(__0);
            if (sourceCount <= 0) return;

            CopyListItems(__0, __7);
        }
        catch (Exception ex)
        {
            Mod.Log.Warning($"[questsize] spawn filter relax failed: {ex.Message}");
        }
    }

    // ── allowlist matching (normalize: lowercase-alnum, strip filler) ──────
    private static List<string>? _wanted;

    private static bool IsAllowed(string configName)
    {
        _wanted ??= BuildWanted();
        if (_wanted.Count == 0) return false;
        var have = Norm(configName);
        foreach (var w in _wanted)
            if (have.Contains(w)) return true;
        return false;
    }

    private static List<string> BuildWanted()
    {
        var list = new List<string>();
        foreach (var entry in Mod.Cfg.QuestMaxTeamSizeRooms)
        {
            var nrm = Norm(entry);
            if (nrm.Length > 0) list.Add(nrm);
        }
        return list;
    }

    private static string Norm(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (var c in s)
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
        return sb.ToString().Replace("the", "").Replace("for", "").Replace("of", "");
    }

    // ── reflection helpers (Il2CppInterop exposes il2cpp members as either a
    //    property or a field depending on version — try both) ────────────────
    private static object? GetMember(object o, string name)
    {
        var t = o.GetType();
        var p = t.GetProperty(name, BF);
        if (p != null && p.CanRead && p.GetIndexParameters().Length == 0)
            { try { return p.GetValue(o); } catch { } }
        var f = t.GetField(name, BF);
        if (f != null) { try { return f.GetValue(o); } catch { } }
        return null;
    }

    private static int ReadInt(object? o, string name)
    {
        if (o is null) return 0;
        var v = GetMember(o, name);
        if (v is int n) return n;
        return v != null && int.TryParse(v.ToString(), out var p) ? p : 0;
    }

    private static bool WriteInt(object o, string name, int val)
    {
        var t = o.GetType();
        var p = t.GetProperty(name, BF);
        if (p != null && p.CanWrite) { try { p.SetValue(o, val); return true; } catch { } }
        var f = t.GetField(name, BF);
        if (f != null) { try { f.SetValue(o, val); return true; } catch { } }
        return false;
    }

    private static int ArrLen(object? arr)
    {
        if (arr is null) return 0;
        if (arr is Array a) return a.Length;
        var lp = arr.GetType().GetProperty("Length") ?? arr.GetType().GetProperty("Count");
        return (lp?.GetValue(arr) as int?) ?? 0;
    }

    private static int Count(object? collection) => ArrLen(collection);

    private static object? ArrItem(object? arr, int i)
    {
        if (arr is null) return null;
        if (arr is Array a) return a.GetValue(i);
        var gi = arr.GetType().GetMethod("get_Item", new[] { typeof(int) });
        return gi?.Invoke(arr, new object[] { i });
    }

    // Write a (boxed) struct element back into an Il2CppStructArray via its
    // set_Item(int, T) indexer. Reflection unboxes to T on invoke.
    private static bool SetArrItem(object arr, int i, object val)
    {
        if (arr is Array a) { try { a.SetValue(val, i); return true; } catch { return false; } }
        foreach (var m in arr.GetType().GetMethods())
        {
            if (m.Name != "set_Item") continue;
            var ps = m.GetParameters();
            if (ps.Length == 2 && ps[0].ParameterType == typeof(int))
            {
                try { m.Invoke(arr, new[] { i, val }); return true; } catch { }
            }
        }
        return false;
    }

    private static int CopyListItems(object? src, object? dst)
    {
        if (src is null || dst is null) return 0;

        MethodInfo? add = null;
        foreach (var method in dst.GetType().GetMethods())
        {
            if (method.Name != "Add") continue;
            if (method.GetParameters().Length != 1) continue;
            add = method;
            break;
        }
        if (add is null) return 0;

        var n = ArrLen(src);
        var copied = 0;
        for (var i = 0; i < n; i++)
        {
            var item = ArrItem(src, i);
            if (item is null) continue;
            try
            {
                add.Invoke(dst, new[] { item });
                copied++;
            }
            catch { }
        }
        return copied;
    }

    private static string TypeName(object? value) =>
        value?.GetType().FullName ?? "<null>";
}
