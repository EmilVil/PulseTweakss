using System.Windows;
using Pulse.Services;
using Pulse.Views;

namespace Pulse;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var window = new MainWindow(JsonStore.ForCurrentUser());
        MainWindow = window;
        window.Show();
    }
}
