using System.Text.Json;
using System.Text.Json.Serialization;
using Photino.NET;

namespace DorkNet.Launcher.Backend;

/// <summary>The JS ↔ C# message router. Both sides use a small JSON
/// envelope:
/// <code>{ "type": "command-or-event-name", "payload": { ... }, "requestId": "optional-string" }</code>
/// On the JS side, <c>window.external.sendMessage(JSON.stringify(envelope))</c>
/// pushes to C#; <c>window.external.receiveMessage(cb)</c> registers
/// the inbound handler. On C# side, this class consumes the inbound
/// envelopes via <see cref="HandleAsync"/> and routes by type.</summary>
public sealed class MessageBridge
{
    private readonly PhotinoWindow _window;
    private readonly AppState _state;
    private readonly ReleaseDownloader _releases = new();
    private readonly ClientPatcher _patcher = new();
    private readonly ServerProcess _server = new();
    private Tunnel? _tunnel;
    private VersionsManifest? _manifestCache;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public MessageBridge(PhotinoWindow window)
    {
        _window = window;
        _state = AppState.Load();
    }

    public async Task HandleAsync(string raw)
    {
        var envelope = JsonSerializer.Deserialize<Envelope>(raw, JsonOptions);
        if (envelope is null) return;

        // Dispatch table. Add new commands here. Keep handlers tight —
        // long work goes in Backend/* classes, not inline.
        switch (envelope.Type)
        {
            case "init":
                await InitAsync(envelope);
                break;
            case "set-mode":
                _state.Mode = Enum.TryParse<AppMode>(envelope.Payload?.GetProperty("mode").GetString(), true, out var m) ? m : AppMode.Unset;
                _state.Save();
                SendEvent("state-changed", _state);
                break;
            case "pick-recroom":
                var picked = RecRoomPicker.Pick(_state.RecRoomPath);
                if (picked is not null)
                {
                    var (ok, reason) = RecRoomPicker.Validate(picked);
                    if (!ok) { SendEvent("error", new { source = "pick-recroom", message = reason }); break; }
                    _state.RecRoomPath = picked;
                    _state.Save();
                    SendEvent("state-changed", _state);
                }
                break;
            case "set-photon":
                ApplyPhoton(envelope.Payload);
                _state.Save();
                SendEvent("state-changed", _state);
                break;
            case "set-server-name":
                _state.ServerName = envelope.Payload?.GetProperty("name").GetString() ?? "";
                _state.Save();
                SendEvent("state-changed", _state);
                break;
            case "fetch-versions":
                _manifestCache = await new VersionsManifest().FetchAsync();
                SendEvent("versions", _manifestCache);
                break;
            case "host-start":
                await HostStartAsync(envelope);
                break;
            case "host-stop":
                await HostStopAsync();
                break;
            case "decode-join-code":
                var decoded = JoinCode.Decode(envelope.Payload?.GetProperty("code").GetString() ?? "");
                SendEvent("join-code-decoded", decoded);
                break;
            case "join-apply":
                await JoinApplyAsync(envelope);
                break;
            default:
                SendEvent("error", new { source = "bridge", message = $"unknown command: {envelope.Type}" });
                break;
        }
    }

    private async Task InitAsync(Envelope envelope)
    {
        SendEvent("state-changed", _state);
        _manifestCache = await new VersionsManifest().FetchAsync();
        SendEvent("versions", _manifestCache);
    }

    private void ApplyPhoton(JsonElement? payload)
    {
        if (payload is not { } p) return;
        if (p.TryGetProperty("appId", out var a)) _state.PhotonAppId = a.GetString() ?? "";
        if (p.TryGetProperty("voiceAppId", out var v)) _state.PhotonVoiceAppId = v.GetString() ?? "";
        if (p.TryGetProperty("region", out var r)) _state.PhotonRegion = r.GetString() ?? "us";
    }

    private async Task HostStartAsync(Envelope envelope)
    {
        if (_manifestCache is null)
            _manifestCache = await new VersionsManifest().FetchAsync();
        var versionKey = envelope.Payload?.GetProperty("versionKey").GetString();
        var version = _manifestCache?.Branches.FirstOrDefault(b => b.VersionKey == versionKey);
        if (version is null) { SendEvent("error", new { source = "host-start", message = $"unknown version: {versionKey}" }); return; }
        if (string.IsNullOrEmpty(_state.PhotonAppId)) { SendEvent("error", new { source = "host-start", message = "Photon AppId not set — fill it in under Settings." }); return; }

        SendEvent("host-status", new { stage = "downloading-server" });
        var progress = new Progress<DownloadProgress>(p =>
            SendEvent("download-progress", new { fraction = p.Fraction, bytes = p.BytesRead, total = p.TotalBytes }));
        var serverDir = await _releases.EnsureServerAsync(version, progress);

        SendEvent("host-status", new { stage = "opening-tunnel" });
        _tunnel = new Tunnel();
        var publicUrl = await _tunnel.StartAsync(localPort: 5005);
        var apex = new Uri(publicUrl).Host;

        SendEvent("host-status", new { stage = "starting-server" });
        await _server.StartAsync(serverDir, _state, apex);
        _state.SelectedVersion = version.VersionKey;
        _state.Save();

        SendEvent("host-status", new { stage = "patching-client" });
        var patcherDir = await _releases.EnsurePatcherAsync(version, progress);
        var patchResult = await _patcher.ApplyAsync(
            patcherDir,
            _state.RecRoomPath ?? "",
            _state.PhotonAppId,
            _state.PhotonVoiceAppId,
            apex);

        if (!patchResult.Ok)
        {
            SendEvent("error", new { source = "patcher", message = patchResult.Log });
            return;
        }

        var joinCode = JoinCode.Encode(new JoinPayload
        {
            Host = apex,
            VersionKey = version.VersionKey,
            PhotonAppId = _state.PhotonAppId,
            PhotonVoiceAppId = _state.PhotonVoiceAppId,
            PhotonRegion = _state.PhotonRegion,
            Name = _state.ServerName,
        });

        SendEvent("host-status", new { stage = "ready", publicUrl, joinCode });
    }

    private async Task HostStopAsync()
    {
        await _server.StopAsync();
        if (_tunnel is not null) { await _tunnel.DisposeAsync(); _tunnel = null; }
        SendEvent("host-status", new { stage = "stopped" });
    }

    private async Task JoinApplyAsync(Envelope envelope)
    {
        var code = envelope.Payload?.GetProperty("code").GetString();
        var payload = JoinCode.Decode(code ?? "");
        if (payload is null) { SendEvent("error", new { source = "join", message = "invalid join code" }); return; }
        if (string.IsNullOrEmpty(_state.RecRoomPath)) { SendEvent("error", new { source = "join", message = "pick your Rec Room install first" }); return; }
        if (_manifestCache is null)
            _manifestCache = await new VersionsManifest().FetchAsync();
        var version = _manifestCache?.Branches.FirstOrDefault(b => b.VersionKey == payload.VersionKey);
        if (version is null) { SendEvent("error", new { source = "join", message = $"host is on a version this launcher doesn't know about: {payload.VersionKey}" }); return; }

        SendEvent("join-status", new { stage = "downloading-patcher" });
        var progress = new Progress<DownloadProgress>(p =>
            SendEvent("download-progress", new { fraction = p.Fraction, bytes = p.BytesRead, total = p.TotalBytes }));
        var patcherDir = await _releases.EnsurePatcherAsync(version, progress);

        SendEvent("join-status", new { stage = "patching-client" });
        var patchResult = await _patcher.ApplyAsync(
            patcherDir,
            _state.RecRoomPath!,
            payload.PhotonAppId,
            payload.PhotonVoiceAppId,
            payload.Host);

        if (!patchResult.Ok)
        {
            SendEvent("error", new { source = "patcher", message = patchResult.Log });
            return;
        }

        SendEvent("join-status", new { stage = "ready" });
    }

    public void SendEvent(string type, object? payload)
    {
        var json = JsonSerializer.Serialize(
            new { type, payload },
            JsonOptions);
        _window.SendWebMessage(json);
    }

    private sealed class Envelope
    {
        [JsonPropertyName("type")] public string Type { get; set; } = "";
        [JsonPropertyName("payload")] public JsonElement? Payload { get; set; }
        [JsonPropertyName("requestId")] public string? RequestId { get; set; }
    }
}
