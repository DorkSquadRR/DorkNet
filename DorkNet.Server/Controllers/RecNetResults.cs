using DorkNet.Models.Auth;
using Microsoft.AspNetCore.Mvc;

namespace DorkNet.Server.Controllers;

/// <summary>
/// Bodies for the 2023 client's <c>RecNetResult</c> writes.
///
/// A lot of the client's fire-and-forget writes look status-only from their
/// signature (they return <c>LDGADANDBIO</c>, the untyped promise), but they
/// are not: they go through <c>BFDGEKKNAPB.PEFKBAEAOPD</c>
/// (<c>RecNet.Runtime/BFDGEKKNAPB.txt:353</c>, "ExpandRecNetResult"), which
/// deserialises the body as <c>DJMHAFPGLLN</c> = <c>{Success, Error}</c>
/// (formatter <c>RecNet.Runtime/LMCCNLLHLCJ.txt:191-218</c> registers
/// <c>Success</c>/<c>success</c> and <c>Error</c>/<c>error</c>) and then runs
/// the <c>&lt;ExpandRecNetResult&gt;b__2_0</c> lambda
/// (<c>RecNet.Runtime/BFDGEKKNAPB_NestedType___c.txt:61</c>):
/// <list type="bullet">
/// <item>body null (an empty 200 or <c>null</c> — <c>COLNKALCBOK.txt</c>
/// JIKCMNMKNPH returns default for both) → NullReferenceException, the write
/// shows "Failed to …" although the server applied it;</item>
/// <item><c>Success</c> missing/false → <c>Promise.Rejected(Error)</c>; with no
/// <c>Error</c> key that throws <c>ArgumentException: Error message cannot be
/// null</c> (seen in Player.log as an unobserved task exception).</item>
/// </list>
/// So every such endpoint must answer <c>{"success":true,"error":""}</c> on
/// success, and carry a non-null <c>error</c> on a 200 failure.
/// </summary>
public static class RecNetResults
{
    public static IActionResult Ok() =>
        new OkObjectResult(new RecNetResult { Success = true, Error = string.Empty });

    public static IActionResult Fail(string error) =>
        new OkObjectResult(new RecNetResult { Success = false, Error = error });
}
