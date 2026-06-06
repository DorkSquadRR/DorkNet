namespace DorkNet.Server;

/// <summary>
/// Canonical "far future / never expires" sentinel dates for anything we
/// serialise back to the watch.
///
/// DO NOT use <c>DateTime.MaxValue</c> or year 9999 in client-facing wire
/// data. The 2020.12 watch parses ISO-8601 timestamps and adjusts them to the
/// player's LOCAL timezone (DateTimeParse.ParseISO8601 → AdjustTimeZoneToLocal
/// → TimeZoneInfo DST resolution). For a player whose timezone has DST
/// adjustment rules, resolving the DST transition for a year-9999 instant
/// pushes the computed transition point past <c>DateTime.MaxValue</c> (year
/// 10000) and throws <c>ArgumentOutOfRangeException: Year, Month, and Day
/// parameters describe an un-representable DateTime</c>. That blows up the
/// WHOLE response deserialization → "Received malformed RecNet response" → the
/// store / gift drops / promo board silently break, but ONLY for players in
/// the affected timezones (everyone else parses the same payload fine, which
/// makes it look like a per-player gremlin).
///
/// 2099 is effectively "forever" for a 2020-era client and is nowhere near the
/// overflow boundary in any timezone.
/// </summary>
public static class WireDates
{
    /// <summary>Far-future sentinel as a <see cref="DateTime"/> (UTC).</summary>
    public static readonly DateTime FarFuture =
        new(2099, 12, 31, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Far-future sentinel pre-rendered as the ISO-8601 Z string the
    /// watch expects (used where the DTO field is a raw string).</summary>
    public const string FarFutureIso = "2099-12-31T00:00:00Z";
}
