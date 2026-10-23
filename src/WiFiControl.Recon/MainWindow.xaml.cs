using System.Collections.ObjectModel;
using System.Windows;
using WiFiControl.Recon.Diagnostics;

namespace WiFiControl.Recon;

internal partial class MainWindow : Window
{
    private readonly ReconLog _log;
    private readonly ObservableCollection<Reading> _readings = [];

    public MainWindow(ReconLog log)
    {
        _log = log;
        InitializeComponent();
        Title += " " + AppInfo.Version;
        ReadingsList.ItemsSource = _readings;
        Loaded += async (_, _) => await ReadAsync("при запуске");
    }

    private async void OnReadAgain(object sender, RoutedEventArgs e)
    {
        await ReadAsync("по кнопке «Прочитать заново»");
    }

    // Чтение идёт вне потока окна: дальше в нём появятся ожидание сканирования и медленные вызовы WMI
    private async Task ReadAsync(string occasion)
    {
        ReadAgainButton.IsEnabled = false;
        Status.Text = "Читаю…";
        try
        {
            Reading reading = await Task.Run(() => _log.Read(occasion));
            _readings.Add(reading);
            ReadingsScroll.ScrollToEnd();
            Status.Text = "Чтение " + reading.Number + (reading.HasErrors ? " — есть ошибки" : "");
        }
        finally
        {
            ReadAgainButton.IsEnabled = true;
        }
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        try
        {
            string path = _log.Save(AppContext.BaseDirectory);
            Status.Text = "Отчёт сохранён: " + path;
            Status.ToolTip = path;
        }
        catch (Exception ex)
        {
            Status.Text = "Не удалось сохранить отчёт: " + ex.Message;
        }
    }
}
