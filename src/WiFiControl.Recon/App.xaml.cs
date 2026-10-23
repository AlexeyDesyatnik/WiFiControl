using System.Windows;
using System.Windows.Threading;
using WiFiControl.Recon.Diagnostics;
using WiFiControl.Recon.Model;
using WiFiControl.Recon.Windows;

namespace WiFiControl.Recon;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;

        var log = new ReconLog(new WindowsSystem(), new SystemClock(), AppInfo.Version);
        new MainWindow(log).Show();
    }

    // Разведка должна дожить до сохранения отчёта, поэтому сбой показывается, но не закрывает окно
    private static void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show("Ошибка: " + e.Exception.GetType().Name + ": " + e.Exception.Message, "WiFiControl — разведка", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
