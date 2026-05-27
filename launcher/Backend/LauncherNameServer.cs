using System.Net;
using System.Net.Sockets;
using System.Text;

namespace DorkNet.Launcher.Backend;

/// <summary>Minimal localhost HTTP name-server for the patched client.
/// The patcher points the hardcoded RecNet bootstrap URL at
/// http://127.0.0.1:80; this listener answers that request with the
/// service URL map for the active Easy Launcher session.</summary>
public sealed class LauncherNameServer : IAsyncDisposable
{
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private string _publicBaseUrl = "";

    public void Start(string publicBaseUrl)
    {
        _publicBaseUrl = SingleOriginServiceMap.NormalizeBaseUrl(publicBaseUrl);
        if (_listener is not null) return;

        _cts = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Loopback, 80);
        _listener.Start();
        _loop = Task.Run(() => AcceptLoopAsync(_cts.Token));
    }

    public void Update(string publicBaseUrl)
        => _publicBaseUrl = SingleOriginServiceMap.NormalizeBaseUrl(publicBaseUrl);

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener is not null)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(ct); }
            catch (OperationCanceledException) { break; }
            catch { if (ct.IsCancellationRequested) break; else continue; }
            _ = Task.Run(() => HandleAsync(client, ct), ct);
        }
    }

    private async Task HandleAsync(TcpClient client, CancellationToken ct)
    {
        using var clientHandle = client;
        try
        {
            await using var stream = client.GetStream();
            var buffer = new byte[2048];
            _ = await stream.ReadAsync(buffer, ct);

            var body = SingleOriginServiceMap.ToJson(_publicBaseUrl);
            var bodyBytes = Encoding.UTF8.GetBytes(body);
            var header =
                "HTTP/1.1 200 OK\r\n" +
                "Content-Type: application/json; charset=utf-8\r\n" +
                $"Content-Length: {bodyBytes.Length}\r\n" +
                "Cache-Control: no-store\r\n" +
                "Connection: close\r\n\r\n";
            var headerBytes = Encoding.ASCII.GetBytes(header);
            await stream.WriteAsync(headerBytes, ct);
            await stream.WriteAsync(bodyBytes, ct);
        }
        catch { }
    }

    public async ValueTask DisposeAsync()
    {
        try { _cts?.Cancel(); } catch { }
        try { _listener?.Stop(); } catch { }
        if (_loop is not null)
        {
            try { await _loop; } catch { }
        }
        _cts?.Dispose();
        _listener = null;
        _cts = null;
        _loop = null;
    }
}
