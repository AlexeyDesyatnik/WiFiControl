using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using WiFiControl.Recon.Diagnostics;

namespace WiFiControl.Recon;

internal partial class MainWindow : Window
{
    private readonly ReconLog _log;
    private readonly ObservableCollection<Reading> _readings = [];
    private readonly ObservableCollection<RecordedEvent> _events = [];
    private IDisposable? _watching;

    public MainWindow(ReconLog log)
    {
        _log = log;
        InitializeComponent();
        Title += " " + AppInfo.Version;
        ReadingsList.ItemsSource = _readings;
        EventsList.ItemsSource = _events;

        Loaded += async (_, _) =>
        {
            // Лента ведётся с самого начала: в неё попадает и сканирование первого чтения
            _log.EventRecorded += OnEventRecorded;
            _watching = _log.StartWatching();
            await ReadAsync("при запуске");
        };
        Closed += (_, _) =>
        {
            _log.EventRecorded -= OnEventRecorded;
            _watching?.Dispose();
        };
    }

    // Приходит из потоков WLAN API, сетевого стека и SystemEvents; окно обновляется, не дожидаясь отрисовки
    private void OnEventRecorded(RecordedEvent recorded)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            _events.Add(recorded);
            EventsHeader.Text = "Лента событий: " + _events.Count;
            EventsList.ScrollIntoView(recorded);
        });
    }

    private async void OnReadAgain(object sender, RoutedEventArgs e)
    {
        await ReadAsync("по кнопке «Прочитать заново»");
    }

    // Чтение идёт вне потока окна: в нём ожидание сканирования и медленные вызовы WMI
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
