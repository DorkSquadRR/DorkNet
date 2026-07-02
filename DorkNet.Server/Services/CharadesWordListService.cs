using System.Text.Json;
using System.Text.Json.Serialization;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace DorkNet.Server.Services;

/// <summary>
/// Admin-managed library of 3D Charades word lists plus the live
/// slot→list bindings the March 2023 client reads.
///
/// <para>Wire contract (verified in the 2023.03.21 il2cpp dump —
/// <c>CardBox</c> / <c>GEFMIBEPMKJ</c>): the client GETs
/// <c>api/activities/charades/v1/words/{source}</c> at card-box spawn,
/// where <c>{source}</c> is one of three baked <c>cardSource</c> enum
/// slots (<see cref="CharadesSlot"/>). The response is a JSON array of
/// <c>{ "EN_US": string, "Difficulty": int }</c> where Difficulty is the
/// client <c>CNMMMNJJDMM</c> enum (<see cref="DifficultyEasy"/> etc.).</para>
///
/// <para>Storage: each list is a <see cref="CharadesWordListEntity"/> row
/// (unlimited library); the live slot bindings live in
/// <see cref="ServerSettingsEntity.CharadesSlotBindingsJson"/> via
/// <see cref="ServerSettingsService"/>. Switching a slot just repoints its
/// binding — no redeploy, effective on the next card-box refresh.</para>
/// </summary>
public class CharadesWordListService(DorkNetDbContext db, ServerSettingsService settings)
{
    /// <summary>The three baked <c>CardBox.cardSource</c> values the client
    /// can request. Integer values match the client
    /// <c>GEFMIBEPMKJ.EODJJDILECA</c> enum.</summary>
    public enum CharadesSlot
    {
        Charades = 0,
        CharadesAprilFoolsDay = 1,
        Icebreakers = 2,
    }

    // Client CNMMMNJJDMM difficulty enum values.
    public const int DifficultyEasy = 0;
    public const int DifficultyHard = 1;
    public const int DifficultyVeryHard = 10;
    public const int DifficultyIcebreaker = 20;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>One card as stored/edited in the admin UI.</summary>
    public sealed record CharadesWord(string Text, int Difficulty);

    /// <summary>A word list as returned to the admin SPA.</summary>
    public sealed record CharadesWordListDto(
        long Id,
        string Name,
        IReadOnlyList<CharadesWord> Words,
        bool IsBuiltIn,
        DateTime UpdatedAt);

    /// <summary>Wire entry. Property names are pinned to the exact casing
    /// the client's LitJson importer reads (<c>EN_US</c>, <c>Difficulty</c>);
    /// LitJson key lookup is case-sensitive.</summary>
    public sealed record CharadesWordWire(
        [property: JsonPropertyName("EN_US")] string EN_US,
        [property: JsonPropertyName("Difficulty")] int Difficulty);

    // ---- library CRUD ----------------------------------------------------

    public async Task<IReadOnlyList<CharadesWordListDto>> GetAllAsync()
    {
        var rows = await db.CharadesWordLists.AsNoTracking()
            .OrderByDescending(l => l.IsBuiltIn)
            .ThenBy(l => l.Id)
            .ToListAsync();
        return rows.Select(ToDto).ToList();
    }

    public async Task<CharadesWordListDto?> GetAsync(long id)
    {
        var row = await db.CharadesWordLists.AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == id);
        return row is null ? null : ToDto(row);
    }

    public async Task<CharadesWordListDto> CreateAsync(
        string name, IReadOnlyList<CharadesWord>? words = null, bool isBuiltIn = false)
    {
        var row = new CharadesWordListEntity
        {
            Name = CleanName(name),
            WordsJson = SerializeWords(NormalizeWords(words ?? [])),
            IsBuiltIn = isBuiltIn,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.CharadesWordLists.Add(row);
        await db.SaveChangesAsync();
        return ToDto(row);
    }

    /// <summary>Rename and/or replace the words of a list. Null args are
    /// left untouched.</summary>
    public async Task<CharadesWordListDto?> UpdateAsync(
        long id, string? name, IReadOnlyList<CharadesWord>? words)
    {
        var row = await db.CharadesWordLists.FirstOrDefaultAsync(l => l.Id == id);
        if (row is null) return null;
        if (name is not null) row.Name = CleanName(name);
        if (words is not null) row.WordsJson = SerializeWords(NormalizeWords(words));
        row.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return ToDto(row);
    }

    public async Task<bool> DeleteAsync(long id)
    {
        var row = await db.CharadesWordLists.FirstOrDefaultAsync(l => l.Id == id);
        if (row is null) return false;
        db.CharadesWordLists.Remove(row);
        await db.SaveChangesAsync();

        // If any slot was pointing at the list we just deleted, drop the
        // dangling binding so resolution falls back cleanly.
        var bindings = await settings.GetCharadesSlotBindingsAsync();
        if (bindings.Charades == id || bindings.CharadesAprilFoolsDay == id || bindings.Icebreakers == id)
        {
            await settings.SetCharadesSlotBindingsAsync(new CharadesSlotBindings(
                bindings.Charades == id ? 0 : bindings.Charades,
                bindings.CharadesAprilFoolsDay == id ? 0 : bindings.CharadesAprilFoolsDay,
                bindings.Icebreakers == id ? 0 : bindings.Icebreakers));
        }
        return true;
    }

    /// <summary>Append (or replace) cards parsed from pasted text.
    /// One phrase per line; an optional <c>| difficulty</c> suffix per line
    /// (easy/hard/veryhard/icebreaker or a raw int) overrides
    /// <paramref name="defaultDifficulty"/>.</summary>
    public async Task<CharadesWordListDto?> ImportAsync(
        long id, string pasteText, int defaultDifficulty, bool replace)
    {
        var row = await db.CharadesWordLists.FirstOrDefaultAsync(l => l.Id == id);
        if (row is null) return null;

        var incoming = ParsePaste(pasteText, defaultDifficulty);
        var combined = replace
            ? incoming
            : DeserializeWords(row.WordsJson).Concat(incoming).ToList();

        row.WordsJson = SerializeWords(NormalizeWords(combined));
        row.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return ToDto(row);
    }

    // ---- slot bindings ---------------------------------------------------

    public Task<CharadesSlotBindings> GetBindingsAsync() => settings.GetCharadesSlotBindingsAsync();

    public async Task<CharadesSlotBindings> SetBindingAsync(CharadesSlot slot, long listId)
    {
        // A listId of 0 clears the binding (falls back to the built-in for
        // that slot). A non-zero id must reference an existing list.
        if (listId != 0 && !await db.CharadesWordLists.AnyAsync(l => l.Id == listId))
            throw new ArgumentException($"No charades word list with id {listId}.");

        var current = await settings.GetCharadesSlotBindingsAsync();
        var next = slot switch
        {
            CharadesSlot.Charades => current with { Charades = listId },
            CharadesSlot.CharadesAprilFoolsDay => current with { CharadesAprilFoolsDay = listId },
            CharadesSlot.Icebreakers => current with { Icebreakers = listId },
            _ => current,
        };
        return await settings.SetCharadesSlotBindingsAsync(next);
    }

    // ---- client resolution ----------------------------------------------

    /// <summary>Resolve the wire deck for a client-requested source. The
    /// route param may be the enum name (<c>Charades</c>) or its int
    /// (<c>0</c>); unknown values fall back to the Charades slot. Returns
    /// the bound list's cards, or that slot's built-in seed list if the
    /// binding is empty/dangling, or an empty array as a last resort.</summary>
    public async Task<IReadOnlyList<CharadesWordWire>> ResolveWireWordsAsync(string source)
    {
        var slot = ParseSlot(source);
        var bindings = await settings.GetCharadesSlotBindingsAsync();
        var boundId = slot switch
        {
            CharadesSlot.CharadesAprilFoolsDay => bindings.CharadesAprilFoolsDay,
            CharadesSlot.Icebreakers => bindings.Icebreakers,
            _ => bindings.Charades,
        };

        CharadesWordListEntity? row = null;
        if (boundId != 0)
            row = await db.CharadesWordLists.AsNoTracking().FirstOrDefaultAsync(l => l.Id == boundId);

        // Fallback: the built-in list whose name matches the slot's default.
        row ??= await db.CharadesWordLists.AsNoTracking()
            .Where(l => l.IsBuiltIn && l.Name == DefaultListName(slot))
            .FirstOrDefaultAsync();

        var words = row is null ? [] : DeserializeWords(row.WordsJson);
        return words.Select(w => new CharadesWordWire(w.Text, w.Difficulty)).ToList();
    }

    // ---- seeding ---------------------------------------------------------

    /// <summary>Seed the three built-in lists (Default / April Fools /
    /// Icebreakers) and point each slot at them. Idempotent: no-ops once any
    /// list exists, so admin edits are never clobbered on restart.</summary>
    public async Task SeedAsync()
    {
        if (await db.CharadesWordLists.AnyAsync()) return;

        var defaultList = await CreateAsync(
            DefaultListName(CharadesSlot.Charades),
            DefaultCharadesWords, isBuiltIn: true);
        var aprilList = await CreateAsync(
            DefaultListName(CharadesSlot.CharadesAprilFoolsDay),
            AprilFoolsWords, isBuiltIn: true);
        var iceList = await CreateAsync(
            DefaultListName(CharadesSlot.Icebreakers),
            IcebreakerWords, isBuiltIn: true);

        await settings.SetCharadesSlotBindingsAsync(new CharadesSlotBindings(
            defaultList.Id, aprilList.Id, iceList.Id));
    }

    // ---- helpers ---------------------------------------------------------

    public static IReadOnlyList<CharadesWord> ParsePaste(string text, int defaultDifficulty)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var result = new List<CharadesWord>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim().TrimEnd('\r').Trim();
            if (line.Length == 0) continue;

            var difficulty = defaultDifficulty;
            var pipe = line.LastIndexOf('|');
            if (pipe > 0 && pipe < line.Length - 1)
            {
                var maybe = line[(pipe + 1)..].Trim();
                if (TryParseDifficulty(maybe, out var d))
                {
                    difficulty = d;
                    line = line[..pipe].Trim();
                }
            }
            if (line.Length == 0) continue;
            result.Add(new CharadesWord(line, difficulty));
        }
        return result;
    }

    private static bool TryParseDifficulty(string s, out int difficulty)
    {
        switch (s.Replace(" ", string.Empty).ToLowerInvariant())
        {
            case "easy": difficulty = DifficultyEasy; return true;
            case "hard": difficulty = DifficultyHard; return true;
            case "veryhard": case "vhard": case "stupidhard": difficulty = DifficultyVeryHard; return true;
            case "icebreaker": case "icebreakers": difficulty = DifficultyIcebreaker; return true;
        }
        if (int.TryParse(s, out var n) &&
            n is DifficultyEasy or DifficultyHard or DifficultyVeryHard or DifficultyIcebreaker)
        {
            difficulty = n;
            return true;
        }
        difficulty = DifficultyEasy;
        return false;
    }

    private static CharadesSlot ParseSlot(string source)
    {
        if (string.IsNullOrWhiteSpace(source)) return CharadesSlot.Charades;
        var s = source.Trim();
        if (int.TryParse(s, out var n) && Enum.IsDefined(typeof(CharadesSlot), n))
            return (CharadesSlot)n;
        if (Enum.TryParse<CharadesSlot>(s, ignoreCase: true, out var slot))
            return slot;
        return CharadesSlot.Charades;
    }

    private static string DefaultListName(CharadesSlot slot) => slot switch
    {
        CharadesSlot.CharadesAprilFoolsDay => "April Fools (impossible)",
        CharadesSlot.Icebreakers => "Icebreakers",
        _ => "Default",
    };

    private static CharadesWordListDto ToDto(CharadesWordListEntity row) => new(
        row.Id, row.Name, DeserializeWords(row.WordsJson), row.IsBuiltIn, row.UpdatedAt);

    private static string CleanName(string name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0) trimmed = "Untitled list";
        return trimmed.Length > 128 ? trimmed[..128] : trimmed;
    }

    /// <summary>Trim, drop blanks, clamp difficulty to a known value, and
    /// de-duplicate case-insensitively (keeping first occurrence).</summary>
    private static IReadOnlyList<CharadesWord> NormalizeWords(IReadOnlyList<CharadesWord> words)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<CharadesWord>();
        foreach (var w in words)
        {
            var text = (w.Text ?? string.Empty).Trim();
            if (text.Length == 0) continue;
            if (text.Length > 256) text = text[..256];
            if (!seen.Add(text)) continue;
            var difficulty = w.Difficulty is DifficultyEasy or DifficultyHard
                or DifficultyVeryHard or DifficultyIcebreaker
                ? w.Difficulty
                : DifficultyEasy;
            result.Add(new CharadesWord(text, difficulty));
        }
        return result;
    }

    private static string SerializeWords(IReadOnlyList<CharadesWord> words) =>
        JsonSerializer.Serialize(words, JsonOptions);

    private static IReadOnlyList<CharadesWord> DeserializeWords(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<CharadesWord>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    // ---- seed content ----------------------------------------------------

    private static CharadesWord E(string t) => new(t, DifficultyEasy);
    private static CharadesWord H(string t) => new(t, DifficultyHard);
    private static CharadesWord V(string t) => new(t, DifficultyVeryHard);
    private static CharadesWord I(string t) => new(t, DifficultyIcebreaker);

    /// <summary>A broad, family-friendly default deck spread across the
    /// three charades difficulties (easy / hard / very hard).</summary>
    private static readonly IReadOnlyList<CharadesWord> DefaultCharadesWords =
    [
        // Easy — single actable words
        E("Dog"), E("Cat"), E("Elephant"), E("Monkey"), E("Snake"), E("Rabbit"),
        E("Penguin"), E("Shark"), E("Spider"), E("Kangaroo"), E("Frog"), E("Horse"),
        E("Running"), E("Swimming"), E("Dancing"), E("Sleeping"), E("Eating"), E("Jumping"),
        E("Crying"), E("Laughing"), E("Sneezing"), E("Fishing"), E("Cooking"), E("Painting"),
        E("Guitar"), E("Piano"), E("Drums"), E("Basketball"), E("Soccer"), E("Tennis"),
        E("Skateboard"), E("Bicycle"), E("Airplane"), E("Rocket"), E("Boat"), E("Train"),
        E("Umbrella"), E("Telephone"), E("Camera"), E("Toothbrush"), E("Scissors"), E("Hammer"),
        E("Pizza"), E("Ice cream"), E("Banana"), E("Popcorn"), E("Birthday cake"), E("Hamburger"),
        E("Robot"), E("Ghost"), E("Zombie"), E("Superhero"), E("Pirate"), E("Wizard"),
        // Hard — phrases, titles, actions
        H("Riding a roller coaster"), H("Brushing your teeth"), H("Tying your shoes"),
        H("Blowing out candles"), H("Milking a cow"), H("Climbing a ladder"),
        H("Walking a tightrope"), H("Playing video games"), H("Taking a selfie"),
        H("Surfing a wave"), H("Building a sandcastle"), H("Chopping wood"),
        H("Conducting an orchestra"), H("Directing traffic"), H("Juggling"),
        H("The Statue of Liberty"), H("A washing machine"), H("A vending machine"),
        H("A traffic light"), H("A grandfather clock"), H("A roller coaster"),
        H("Peter Pan"), H("Sherlock Holmes"), H("Frankenstein"), H("Cinderella"),
        H("Harry Potter"), H("Spider-Man"), H("The Loch Ness Monster"),
        H("Doing laundry"), H("Mowing the lawn"), H("Flying a kite"),
        H("Scuba diving"), H("Rock climbing"), H("Bowling a strike"),
        // Very hard — abstract-but-still-actable, longer titles
        V("Photosynthesis"), V("The theory of relativity"), V("A midlife crisis"),
        V("Rush hour traffic"), V("Herding cats"), V("A house of cards"),
        V("The circle of life"), V("Breaking the fourth wall"), V("A wild goose chase"),
        V("Death and taxes"), V("The domino effect"), V("Time flies when you're having fun"),
        V("A blessing in disguise"), V("Barking up the wrong tree"),
        V("The elephant in the room"), V("Raining cats and dogs"),
        V("A penny for your thoughts"), V("Bite the bullet"),
        V("The tip of the iceberg"), V("Once in a blue moon"),
    ];

    /// <summary>April Fools deck — concepts that are (nearly) impossible to
    /// act out. All flagged very hard.</summary>
    private static readonly IReadOnlyList<CharadesWord> AprilFoolsWords =
    [
        V("The concept of Tuesday"), V("Existential dread"), V("The smell of rain"),
        V("Object-oriented programming"), V("The year 1997"), V("Nostalgia"),
        V("The color infrared"), V("A prime number"), V("Dramatic irony"),
        V("The stock market"), V("Deja vu"), V("The taste of the number seven"),
        V("Quantum entanglement"), V("The feeling of Sunday night"),
        V("Sarcasm"), V("The concept of infinity"), V("Static electricity"),
        V("The sound of one hand clapping"), V("Impostor syndrome"),
        V("The general theory of relativity"), V("A tax audit"),
        V("The plot of Inception"), V("Schadenfreude"), V("The Dewey Decimal System"),
        V("Awkward silence"), V("The gluten in bread"), V("Inflation"),
        V("The concept of a concept"), V("Wi-Fi"), V("The color of Wednesday"),
        V("A recursive function"), V("The heat death of the universe"),
        V("Peripheral vision"), V("The smell of a new car"),
        V("Cryptocurrency"), V("The uncanny valley"), V("A palindrome"),
        V("The passage of time"), V("Cognitive dissonance"),
        V("The concept of nothing at all"),
    ];

    /// <summary>Icebreaker prompts for the Icebreakers card source. All
    /// flagged with the icebreaker difficulty so they group correctly.</summary>
    private static readonly IReadOnlyList<CharadesWord> IcebreakerWords =
    [
        I("Your dream vacation"), I("A hidden talent you have"),
        I("Your favorite childhood snack"), I("The best gift you ever got"),
        I("A superpower you'd want"), I("Your go-to karaoke song"),
        I("The last show you binged"), I("Your comfort food"),
        I("A place you'd love to visit"), I("Your favorite season"),
        I("The best concert you've been to"), I("Your dream job as a kid"),
        I("A weird food combo you love"), I("Your favorite board game"),
        I("The best advice you've received"), I("A skill you'd love to learn"),
        I("Your favorite holiday"), I("The first thing you'd buy if you won the lottery"),
        I("Your most-used emoji"), I("A movie you can quote by heart"),
        I("Your favorite way to relax"), I("The pet you have or wish you had"),
        I("A hobby you picked up recently"), I("Your favorite ice cream flavor"),
        I("The best trip you've ever taken"), I("A song stuck in your head"),
        I("Your favorite thing about Rec Room"), I("The coolest place you've been"),
        I("A talent show act you'd perform"), I("Your dream superpower team-up"),
    ];
}

/// <summary>Which word list (row id) is live for each of the client's
/// three baked charades card-source slots. A 0 means "unbound" — the
/// resolver falls back to that slot's built-in seed list.</summary>
public sealed record CharadesSlotBindings(
    long Charades,
    long CharadesAprilFoolsDay,
    long Icebreakers)
{
    public static CharadesSlotBindings Empty => new(0, 0, 0);
}
