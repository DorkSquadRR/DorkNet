using Photino.NET;
using DorkNet.Launcher.Backend;
using System.Text.Json;

namespace DorkNet.Launcher;

/// <summary>Entry point + the host↔JS message bridge. The window is a
/// PhotinoNET wrapper around WebView2, loading ui/index.html from the
/// app's directory. Every JS ↔ C# call goes through one JSON-tagged
/// envelope so we can route by command/event name without per-call
/// glue.</summary>
public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        AppPaths.EnsureDirectoriesExist();

        var indexPath = Path.Combine(AppContext.BaseDirectory, "ui", "index.html");
        if (!File.Exists(indexPath))
        {
            // First-aid: if the UI files didn't ship next to the binary,
            // the window would be blank with no hint why. Surface the
            // missing-file path before opening anything.
            MessageBox.Error(
                "DorkNet UI files missing",
                $"Could not find {indexPath}. Reinstall the launcher.");
            return;
        }

        var window = new PhotinoWindow()
            .SetTitle("DorkNet")
            .SetUseOsDefaultSize(false)
            .SetSize(900, 640)
            .SetResizable(true)
            .Center()
            .Load(new Uri(indexPath));

        var bridge = new MessageBridge(window);
        window.RegisterWebMessageReceivedHandler((sender, message) =>
        {
            // JS sends JSON envelopes: {"type":"command-name","payload":{...},"requestId":"..."}
            // Reply path is bridge.SendEvent — fire-and-forget unless requestId is set.
            try
            {
                bridge.HandleAsync(message).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                bridge.SendEvent("error", new { source = "bridge", message = ex.Message });
            }
        });

        window.WaitForClose();
    }
}

/// <summary>Minimal MessageBox helper — DorkNet won't pop these often,
/// but a missing-UI-file failure mode needs a visible signal before the
/// PhotinoNET window opens.</summary>
internal static class MessageBox
{
    public static void Error(string title, string body)
    {
        System.Windows.Forms.MessageBox.Show(
            body, title,
            System.Windows.Forms.MessageBoxButtons.OK,
            System.Windows.Forms.MessageBoxIcon.Error);
    }
}
