using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DorkNet.Server.Data;

namespace DorkNet.Server.Controllers.API.Role;

/// <summary>
/// api.rec.net/api/role/* — role / privilege probes the watch consults
/// when deciding whether to surface dev-only UI (the in-watch debug
/// console button, hidden admin-tool tab, etc).
///
/// URL verified against
/// Cpp2IL_ISIL/IsilDump/Assembly-CSharp/RecNet/Accounts.txt:5168
/// (<c>"role/developer/{0}"</c>) called from
/// <c>RecNet.Accounts.GetAccountIsDeveloper</c>. Response shape is a
/// bare JSON boolean — the watch wraps the call with
/// <c>PromiseExtensions.ExpectPrimitiveResponse&lt;bool&gt;</c>, so an
/// array or object response throws InvalidCastException and silently
/// disables the dev UI.
///
/// To make a player a developer (and unlock the watch's
/// debug-console button), set <c>Players.IsDeveloper = 1</c> in the
/// SQLite DB or via the admin UI's player editor.
/// </summary>
[ApiController]
public class RoleController(DorkNetDbContext db) : ControllerBase
{
    // Service-routed call from Accounts.GetAccountIsDeveloper. The
    // 2020 dump's SendRequest carries `Service = 0` (Auth) — verified
    // at Cpp2IL_ISIL/.../RecNet/Accounts.txt:5183-5186 against the
    // RecNet.Service enum in dump.cs:586523-586538 (Auth=0). Service.Auth
    // maps to <c>auth.{apex}</c> in ConfigService's ServiceUrls, and
    // the path the watch appends has NO `/api/` prefix — it's just
    // <c>role/developer/{id}</c>. The previous <c>/api/role/...</c>-only
    // route never matched, so every nameplate probe came back 404 and
    // the watch logged "Failed to GetIsDeveloper for accountId X: HTTP
    // Error 404" once per remote player per scene load.
    [HttpGet("/role/developer/{accountId:long}")]
    [HttpGet("/api/role/developer/{accountId:long}")]
    [AllowAnonymous]
    public async Task<ActionResult<bool>> IsDeveloper(long accountId)
    {
        // OR IsCommunityTeam into the answer so the watch unlocks the
        // in-settings "Developer Display Mode" slider for community-team
        // members too — the slider's positions render the overhead
        // badge as "Community Team" / "Developer" (see
        // Cpp2IL_ISIL/.../PlayerUI.txt:9085-9099). The 2020 watch never
        // probes a separate community-team role, so collapsing both
        // flags into this one endpoint is the only way to reach the
        // badge UI without patching the binary.
        var isDev = await db.Players
            .Where(p => p.Id == accountId)
            .Select(p => (bool?)(p.IsDeveloper || p.IsCommunityTeam || p.IsAdmin))
            .FirstOrDefaultAsync();
        return Ok(isDev ?? false);
    }

    /// <summary>Mirror endpoint for the admin role — same shape, in case
    /// later builds added an <c>/api/role/admin/{id}</c> probe. The 2020
    /// dump only references the developer one but admins also get all
    /// dev privileges, so the result is unioned.</summary>
    [HttpGet("/role/admin/{accountId:long}")]
    [HttpGet("/api/role/admin/{accountId:long}")]
    [AllowAnonymous]
    public async Task<ActionResult<bool>> IsAdmin(long accountId)
    {
        var isAdmin = await db.Players
            .Where(p => p.Id == accountId)
            .Select(p => (bool?)p.IsAdmin)
            .FirstOrDefaultAsync();
        return Ok(isAdmin ?? false);
    }

    /// <summary>Future-proofing for a hypothetical
    /// <c>role/communityteam/{id}</c> probe — not referenced by the 2020
    /// build, but mirrors the developer shape so a patched watch can
    /// distinguish the two roles without a server change.</summary>
    [HttpGet("/role/communityteam/{accountId:long}")]
    [HttpGet("/api/role/communityteam/{accountId:long}")]
    [AllowAnonymous]
    public async Task<ActionResult<bool>> IsCommunityTeam(long accountId)
    {
        var isCt = await db.Players
            .Where(p => p.Id == accountId)
            .Select(p => (bool?)p.IsCommunityTeam)
            .FirstOrDefaultAsync();
        return Ok(isCt ?? false);
    }
}
