using System.Windows;

namespace WiFiControl;

internal partial class RemoveProfileWindow : Window
{
    public RemoveProfileWindow(string profile, IReadOnlyList<string> networks, bool isDefault, string fallback)
    {
        InitializeComponent();
        Question.Text = "Удалить профиль " + profile + "?";
        NetworkList.ItemsSource = networks;
        AffectedNetworks.Visibility = networks.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        Fallback.Text = isDefault
            ? "Привязки к удаляемому профилю будут сняты. Для всех сетей без привязки будет использоваться профиль " + fallback + "."
            : "Привязки этих сетей будут сняты. Для них будет использоваться профиль по умолчанию " + fallback + ".";
        Fallback.Visibility = isDefault || networks.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnRemove(object sender, RoutedEventArgs e) => DialogResult = true;
}
