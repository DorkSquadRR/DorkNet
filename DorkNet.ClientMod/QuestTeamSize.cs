// Raise RRO quest party size past the baked 4.
//
// The cap is NOT in quest-specific code and NOT server-driven — it lives in
// each quest's GameConfigurationAsset.TeamConfigurations[].MaxTeamSize, a
// ScriptableObject baked into the client asset bundle. The quest scoreboard
// (GameScoreboardBodyView instantiates rows into a List) and the party HUD are
// fully dynamic, so they auto-scale once the team cap is raised.
//
// Implemented with reflection (this project references only MelonLoader /
// Harmony / Il2CppInterop.Runtime — NOT the game's interop assemblies), the
// same way DevMenuProbe drives the game. Quest configs load when the quest
// room loads, so we poll from Mod.OnUpdate rather than running once, and only
// touch single-team, quest-named configs so PvP team sizes (paintball/laser
// tag, 2+ teams) are never changed. Every config is logged once so we can
// confirm targeting (these MaxTeamSize values aren't in the static dump).
// Gated behind Cfg.QuestMaxTeamSize.
using System;
using System.Collections.Generic;
using System.Reflection;
using Il2CppInterop.Runtime;

namespace DorkNet.ClientMod;

internal static class QuestTeamSize
{
    private const BindingFlags BF = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private static int _frame;
    private static readonly HashSet<string> _logged = new(StringComparer.Ordinal);

    public static void Tick()
    {
        _frame++;
        if (_frame < 120 || (_frame % 120) != 0) return; // settle, then ~every 2s
        try { Apply(Mod.Cfg.QuestMaxTeamSize); }
        catch (Exception ex) { Mod.Log.Warning($"[questsize] failed: {ex.Message}"); }
    }

    private static void Apply(int target)
    {
        if (target <= 0) return;
        var assetType = Mod.ResolveType("RecRoom.Core.GameManagement.GameConfigurationAsset");
        if (assetType is null) return;

        // Invoke Il2CppType.From via reflection so the Il2CppSystem.Type
        // return value stays boxed as object — calling it directly would
        // need a compile reference to Il2Cppmscorlib, which we don't carry.
        var il2 = ToIl2CppType(assetType);
        if (il2 is null) return;

        var resources = Mod.ResolveType("UnityEngine.Resources");
        var findAll = OneArgStatic(resources, "FindObjectsOfTypeAll", il2);
        var all = findAll?.Invoke(null, new[] { il2 });
        int n = ArrLen(all);
        for (int i = 0; i < n; i++)
        {
            var asset = ArrItem(all, i);
            if (asset is null) continue;
            asset = EnsureConcrete(asset, assetType);

            var name = ReadName(asset);
            var teams = GetMember(asset, "TeamConfigurations");
            if (teams is null) continue;
            int tn = ArrLen(teams);

            if (_logged.Add(name))
            {
                var sizes = new List<string>(tn);
                for (int t = 0; t < tn; t++) sizes.Add(ReadInt(ArrItem(teams, t), "MaxTeamSize").ToString());
                Mod.Log.Msg($"[questsize] config '{name}' teams=[{string.Join(",", sizes)}] (n={tn})");
            }

            // Quests are SINGLE-TEAM co-op. That's the reliable signal: PvP
            // modes have 2+ teams (Paintball [4,4], StuntRunner [1,1,1,1])
            // and Sandbox has many — only quests (the generic "Quest" config
            // AND each per-quest config like "The Rise Of JumboTron") are
            // n==1. Name-matching missed the per-quest configs, so we key off
            // team count instead and leave every 2+-team mode untouched.
            if (tn != 1) continue;

            var changed = false;
            for (int t = 0; t < tn; t++)
            {
                var box = ArrItem(teams, t);
                if (box is null || ReadInt(box, "MaxTeamSize") >= target) continue;
                if (WriteInt(box, "MaxTeamSize", target) && ArrSet(teams, t, box)) changed = true;
            }
            if (changed) Mod.Log.Msg($"[questsize] bumped '{name}' → MaxTeamSize {target}");
        }
    }

    // FindObjectsOfTypeAll returns base-typed elements; downcast to the
    // concrete interop type so reflection sees TeamConfigurations/Name.
    private static object EnsureConcrete(object obj, Type target)
    {
        if (target.IsInstanceOfType(obj)) return obj;
        var bt = obj.GetType();
        while (bt != null && bt.Name != "Il2CppObjectBase") bt = bt.BaseType;
        var cast = bt?.GetMethod("Cast", BindingFlags.Public | BindingFlags.Instance);
        if (cast != null)
        {
            try { return cast.MakeGenericMethod(target).Invoke(obj, null) ?? obj; }
            catch { }
        }
        return obj;
    }

    private static string ReadName(object asset)
    {
        if (GetMember(asset, "Name") is string n && n.Length > 0) return n;
        if (GetMember(asset, "name") is string un && un.Length > 0) return un;
        return "?";
    }

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

    private static object? ArrItem(object? arr, int i)
    {
        if (arr is null) return null;
        if (arr is Array a) return a.GetValue(i);
        var gi = arr.GetType().GetMethod("get_Item", new[] { typeof(int) });
        return gi?.Invoke(arr, new object[] { i });
    }

    private static bool ArrSet(object arr, int i, object val)
    {
        var si = arr.GetType().GetMethod("set_Item", new[] { typeof(int), val.GetType() })
                 ?? arr.GetType().GetMethod("set_Item");
        if (si is null) return false;
        try { si.Invoke(arr, new object[] { i, val }); return true; } catch { return false; }
    }

    private static object? ToIl2CppType(Type managed)
    {
        try
        {
            var t = typeof(Il2CppType);
            var from = t.GetMethod("From", new[] { typeof(Type), typeof(bool) })
                       ?? t.GetMethod("From", new[] { typeof(Type) });
            if (from is null) return null;
            var args = from.GetParameters().Length == 2
                ? new object[] { managed, true } : new object[] { managed };
            return from.Invoke(null, args);
        }
        catch { return null; }
    }

    private static MethodInfo? OneArgStatic(Type? t, string name, object arg)
    {
        if (t is null) return null;
        foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            if (m.Name != name) continue;
            var ps = m.GetParameters();
            if (ps.Length == 1 && ps[0].ParameterType.IsInstanceOfType(arg)) return m;
        }
        return null;
    }
}
