using DorkNet.Server.Services;
using System.Net.Http.Json;
using System.Text.Json;

namespace DorkNet.Server.Tests;

/// <summary>
/// The March-2023 client budgets every CircuitsV2 network send against
/// <c>RecRoom.CircuitsV2.Config.LimitConfig</c>, which it fills from the
/// <c>UGC.CircuitsV2.Limit.*</c> gameconfig keys. Retail ships 5 RPCs per
/// frame; past that the send is dropped and the room save is flagged
/// <c>cv2_rpc_limited</c>. DorkNet serves budgets that lift the cap, and
/// these tests pin both the wire shape the client's Newtonsoft reader
/// binds and the clamps that keep a hand-edited value from crashing it.
/// </summary>
public sealed class CircuitsV2LimitsTests : IClassFixture<DorkNetServerFactory>
{
    private readonly DorkNetServerFactory _factory;

    public CircuitsV2LimitsTests(DorkNetServerFactory factory) => _factory = factory;

    [Fact]
    public async Task Gameconfigs_endpoint_serves_the_network_budget()
    {
        using var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        client.BaseAddress = new Uri($"http://api.{_factory.ApexDomain}");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");

        var rows = await client.GetFromJsonAsync<List<GameConfigurationSetting>>(
            "api/gameconfigs/v1/all");

        var network = rows!.Single(r => r.Key == "UGC.CircuitsV2.Limit.Network");
        var entry = JsonDocument.Parse(network.Value).RootElement.EnumerateArray().Single();
        Assert.True(entry.GetProperty("Rpcs").GetInt32() >= 1_000_000);
        Assert.Equal(0, entry.GetProperty("NumPlayersForActivation").GetInt32());
        // A server that never saved limits (empty column) serves the
        // current defaults, not the retail values.
        Assert.Equal("30000", rows!.Single(r => r.Key == "UGC.CircuitsV2.Limit.Chips").Value);
        Assert.Equal("0", rows!.Single(r => r.Key == "UGC.CircuitsV2.Limit.InkPerChip").Value);
    }

    private static IReadOnlyList<GameConfigurationSetting> Emit(CircuitsV2LimitSettings limits) =>
        ServerSettingsService.ToGameConfigurations(
            ServerSettingsService.DefaultRecCenterDoors(),
            ServerSettingsService.DefaultDiscoveredGameConfigs(),
            limits);

    private static string ValueOf(IReadOnlyList<GameConfigurationSetting> rows, string key) =>
        rows.Single(r => r.Key == key).Value;

    private static JsonElement NetworkOf(CircuitsV2LimitSettings limits) =>
        JsonDocument.Parse(ValueOf(Emit(limits), "UGC.CircuitsV2.Limit.Network")).RootElement;

    [Fact]
    public void Defaults_lift_the_rpc_and_byte_budgets()
    {
        var network = NetworkOf(ServerSettingsService.DefaultCircuitsV2Limits());

        Assert.Equal(JsonValueKind.Array, network.ValueKind);
        var entry = Assert.Single(network.EnumerateArray());
        // Retail is 5 RPCs / 1024 bytes; anything at those values means the
        // budget never actually got lifted.
        Assert.True(entry.GetProperty("Rpcs").GetInt32() >= 1_000_000);
        Assert.True(entry.GetProperty("Bytes").GetInt32() >= 1_000_000);
        Assert.Equal(0, entry.GetProperty("NumPlayersForActivation").GetInt32());
    }

    [Fact]
    public void Network_json_uses_the_field_names_the_client_binds()
    {
        var entry = NetworkOf(ServerSettingsService.DefaultCircuitsV2Limits())
            .EnumerateArray().First();

        // LimitConfig.NetworkConfig's il2cpp field names, verbatim.
        Assert.True(entry.TryGetProperty("NumPlayersForActivation", out _));
        Assert.True(entry.TryGetProperty("Bytes", out _));
        Assert.True(entry.TryGetProperty("BytesTimeWindowFrame60s", out _));
        Assert.True(entry.TryGetProperty("Rpcs", out _));
        Assert.True(entry.TryGetProperty("RpcTimeWindowFrame60s", out _));
    }

    [Fact]
    public void Scalar_limits_serialize_invariant_without_separators()
    {
        var rows = Emit(ServerSettingsService.DefaultCircuitsV2Limits() with
        {
            Chips = 123456,
            CloudValuesAutosaveSeconds = 300f,
        });

        // The client parses these with int/float.TryParse under
        // InvariantCulture, so a grouped or comma-decimal value silently
        // falls back to its baked default.
        Assert.Equal("123456", ValueOf(rows, "UGC.CircuitsV2.Limit.Chips"));
        Assert.Equal("300", ValueOf(rows, "UGC.CircuitsV2.Limit.CloudValuesAutosaveSeconds"));
        Assert.Equal("500000", ValueOf(rows, "UGC.CircuitsV2.Limit.Compute"));
        Assert.Equal("0", ValueOf(rows, "UGC.CircuitsV2.Limit.InkPerChip"));
    }

    [Fact]
    public void Defaults_raise_the_room_size_limits_past_retail()
    {
        var rows = Emit(ServerSettingsService.DefaultCircuitsV2Limits());

        // Retail: Compute 50000, Chips 3000, CloudValues 100,
        // Unculled/VisibleChips 500, InkPerChip 15.
        Assert.Equal("500000", ValueOf(rows, "UGC.CircuitsV2.Limit.Compute"));
        Assert.Equal("30000", ValueOf(rows, "UGC.CircuitsV2.Limit.Chips"));
        Assert.Equal("1000", ValueOf(rows, "UGC.CircuitsV2.Limit.CloudValues"));
        Assert.Equal("3000", ValueOf(rows, "UGC.CircuitsV2.Limit.UnculledChips"));
        Assert.Equal("3000", ValueOf(rows, "UGC.CircuitsV2.Limit.VisibleChips"));
        // InkPerChip is a cost, so the lift is downward.
        Assert.Equal("0", ValueOf(rows, "UGC.CircuitsV2.Limit.InkPerChip"));
        // A save cadence, not a capacity limit: left at retail.
        Assert.Equal("300", ValueOf(rows, "UGC.CircuitsV2.Limit.CloudValuesAutosaveSeconds"));
    }

    [Fact]
    public void Default_visible_chips_do_not_exceed_the_chip_cap()
    {
        var limits = ServerSettingsService.DefaultCircuitsV2Limits();

        // The add-chip path only charges InkPerChip when Chips < VisibleChips
        // (CircuitsV2Manager_NestedType_AMEJFMPHNEO.txt ISIL 228-259), so the
        // defaults keep that path off as well as zeroing the cost.
        Assert.True(limits.VisibleChips <= limits.Chips);
        Assert.True(limits.UnculledChips <= limits.Chips);
    }

    [Fact]
    public void Ink_cost_and_cloud_values_are_capped_so_their_product_fits_an_int()
    {
        var rows = Emit(ServerSettingsService.DefaultCircuitsV2Limits() with
        {
            InkPerChip = int.MaxValue,
            CloudValues = int.MaxValue,
        });

        // The client computes InkPerChip * Math.Min(numChips, CloudValues)
        // in plain int; InkPerChip past the hard-coded 30000-ink room cap
        // also makes a single chip unaffordable.
        var ink = int.Parse(ValueOf(rows, "UGC.CircuitsV2.Limit.InkPerChip"));
        var cloud = int.Parse(ValueOf(rows, "UGC.CircuitsV2.Limit.CloudValues"));
        Assert.Equal(30_000, ink);
        Assert.Equal(10_000, cloud);
        Assert.True((long)ink * cloud < int.MaxValue / 2);
    }

    [Fact]
    public void Negative_ink_cost_is_floored_at_zero()
    {
        var rows = Emit(ServerSettingsService.DefaultCircuitsV2Limits() with { InkPerChip = -5 });

        Assert.Equal("0", ValueOf(rows, "UGC.CircuitsV2.Limit.InkPerChip"));
    }

    [Fact]
    public void Chips_are_bounded_because_the_culling_manager_preallocates_by_them()
    {
        var low = Emit(ServerSettingsService.DefaultCircuitsV2Limits() with { Chips = 0 });
        var high = Emit(ServerSettingsService.DefaultCircuitsV2Limits() with { Chips = int.MaxValue });

        // Four List<Chip>(Chips) at culling-manager construction: 0 or less
        // throws, and int.MaxValue is an out-of-memory kill.
        Assert.Equal("1", ValueOf(low, "UGC.CircuitsV2.Limit.Chips"));
        Assert.Equal("1000000", ValueOf(high, "UGC.CircuitsV2.Limit.Chips"));
    }

    [Fact]
    public void Compute_stays_finite_because_it_is_the_runaway_loop_guard()
    {
        var rows = Emit(ServerSettingsService.DefaultCircuitsV2Limits() with { Compute = int.MaxValue });

        Assert.Equal("10000000", ValueOf(rows, "UGC.CircuitsV2.Limit.Compute"));
    }

    [Fact]
    public void Zero_rpcs_is_clamped_because_the_debug_subscreen_divides_by_it()
    {
        var entry = NetworkOf(ServerSettingsService.DefaultCircuitsV2Limits() with
        {
            Network = [new(0, 1024, 60, Rpcs: 0, RpcTimeWindowFrame60s: 1)],
        }).EnumerateArray().First();

        Assert.True(entry.GetProperty("Rpcs").GetInt32() >= 1);
    }

    [Fact]
    public void Time_windows_are_capped_so_the_ring_buffer_stays_small()
    {
        var entry = NetworkOf(ServerSettingsService.DefaultCircuitsV2Limits() with
        {
            Network = [new(0, 1024, int.MaxValue, 5, int.MaxValue)],
        }).EnumerateArray().First();

        // Each window frame is one int in a client-side ring buffer, so an
        // unbounded window is an out-of-memory kill at circuit init.
        Assert.Equal(3600, entry.GetProperty("RpcTimeWindowFrame60s").GetInt32());
        Assert.Equal(3600, entry.GetProperty("BytesTimeWindowFrame60s").GetInt32());
    }

    [Fact]
    public void Lowest_entry_is_forced_to_activate_at_zero_players()
    {
        var network = NetworkOf(ServerSettingsService.DefaultCircuitsV2Limits() with
        {
            Network =
            [
                new(20, 4096, 60, 50, 1),
                new(8, 2048, 60, 25, 1),
            ],
        });

        // Without an entry at 0 the client rejects the whole array and
        // falls back to the retail 5-RPC budget.
        var entries = network.EnumerateArray().ToList();
        Assert.Equal(0, entries[0].GetProperty("NumPlayersForActivation").GetInt32());
        // The rewrite keeps that entry's own budget.
        Assert.Equal(25, entries[0].GetProperty("Rpcs").GetInt32());
        Assert.Equal(20, entries[1].GetProperty("NumPlayersForActivation").GetInt32());
    }

    [Fact]
    public void Duplicate_thresholds_collapse_and_entries_sort_ascending()
    {
        var network = NetworkOf(ServerSettingsService.DefaultCircuitsV2Limits() with
        {
            Network =
            [
                new(16, 4096, 60, 50, 1),
                new(0, 1024, 60, 10, 1),
                new(16, 8192, 60, 99, 1),
            ],
        });

        var thresholds = network.EnumerateArray()
            .Select(e => e.GetProperty("NumPlayersForActivation").GetInt32())
            .ToList();
        Assert.Equal([0, 16], thresholds);
    }

    [Fact]
    public void Empty_network_list_falls_back_to_the_default_budget()
    {
        var network = NetworkOf(ServerSettingsService.DefaultCircuitsV2Limits() with
        {
            Network = [],
        });

        var entry = Assert.Single(network.EnumerateArray());
        Assert.Equal(0, entry.GetProperty("NumPlayersForActivation").GetInt32());
        Assert.True(entry.GetProperty("Rpcs").GetInt32() >= 1_000_000);
    }
}
