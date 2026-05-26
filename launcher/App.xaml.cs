using System.Windows;
using DorkNet.Launcher.Backend;

namespace DorkNet.Launcher;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        AppPaths.EnsureDirectoriesExist();
        base.OnStartup(e);
    }
}
