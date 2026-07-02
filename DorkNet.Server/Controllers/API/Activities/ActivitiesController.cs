using Microsoft.AspNetCore.Mvc;
using DorkNet.Server.Services;

namespace DorkNet.Server.Controllers.API.Activities;

/// <summary>
/// api.{rec.net,localhost}/api/activities/* — per-activity content
/// (charades words, party-game prompts, etc.).
///
/// The March 2023 watch fetches a 3D Charades deck at card-box spawn from
/// <c>api/activities/charades/v1/words/{source}</c>, where <c>{source}</c>
/// is one of three baked <c>CardBox.cardSource</c> slots — <c>Charades</c>,
/// <c>CharadesAprilFoolsDay</c>, or <c>Icebreakers</c> (verified in the
/// 2023.03.21 il2cpp dump). We resolve the admin-bound word list for the
/// requested slot via <see cref="CharadesWordListService"/> and return the
/// wire array of <c>{ EN_US, Difficulty }</c>. The older December route
/// (no <c>{source}</c>) maps to the default Charades slot.
/// </summary>
[ApiController]
public class ActivitiesController(CharadesWordListService charades) : ControllerBase
{
    [HttpGet("api/activities/charades/v1/words/{source}")]
    public async Task<IActionResult> CharadesWords(string source)
        => Ok(await charades.ResolveWireWordsAsync(source));

    /// <summary>December 2020 shape — no source segment. Serves the default
    /// Charades slot so the older client still gets a populated deck.</summary>
    [HttpGet("api/activities/charades/v1/words")]
    public async Task<IActionResult> CharadesWordsDefault()
        => Ok(await charades.ResolveWireWordsAsync(
            nameof(CharadesWordListService.CharadesSlot.Charades)));
}
