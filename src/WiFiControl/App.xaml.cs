using System.IO;
using System.Windows;
using System.Windows.Threading;
using WiFiControl.Core.Storage;
using WiFiControl.Core.Windows;

namespace WiFiControl;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;

        var store = new ProfileStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WiFiControl"));
        store.Load();

        var wifi = new WindowsWifiSystem();
        var window = new MainWindow(new MainViewModel(new WindowsNetworkSystem(), wifi, store), wifi);
        MainWindow = window;
        window.Show();
    }

    private static void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show("Ошибка: " + e.Exception.GetType().Name + ": " + e.Exception.Message, "WiFiControl", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
