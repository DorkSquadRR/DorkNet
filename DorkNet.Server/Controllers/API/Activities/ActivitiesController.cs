using Microsoft.AspNetCore.Mvc;

namespace DorkNet.Server.Controllers.API.Activities;

/// <summary>
/// api.{rec.net,localhost}/api/activities/* — per-activity content
/// (charades words, party-game prompts, etc.). The 2020 watch
/// fetches these as a static list at room-join time; we ship an
/// empty list so the activity's bundled defaults kick in.
/// </summary>
[ApiController]
public class ActivitiesController : ControllerBase
{
    [HttpGet("api/activities/charades/v1/words")]
    public IActionResult CharadesWords()
    {
        var wordsPath = Path.Combine("TransientData", "charades_words.txt");

        var result = new List<object>();

        if (System.IO.File.Exists(wordsPath))
        {
            foreach (var line in System.IO.File.ReadLines(wordsPath))
            {
                var trimmed = line.Trim();

                if (string.IsNullOrEmpty(trimmed))
                    continue;

                if (trimmed.Contains('|'))
                {
                    var parts = trimmed.Split('|', 2);

                    int difficulty = 0;
                    int.TryParse(parts[1].Trim(), out difficulty);

                    result.Add(new
                    {
                        EN_US = parts[0].Trim(),
                        Difficulty = difficulty
                    });
                }
                else
                {
                    result.Add(new
                    {
                        EN_US = trimmed,
                        Difficulty = 0
                    });
                }
            }
        }

        return Ok(result);
    }
}
