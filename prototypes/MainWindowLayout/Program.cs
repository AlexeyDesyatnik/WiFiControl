// ПРОТОТИП раскладки главного окна (#31): три варианта × несколько состояний.
// Запуск: dotnet run --project prototypes/MainWindowLayout [-- --variant B --state 4]
// ←/→ — вариант, Shift+←/→ — состояние; то же — кнопками на плашке внизу.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using static MainWindowLayout.Ui;

namespace MainWindowLayout;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        var app = new Application { ThemeMode = ThemeMode.System };
        var variant = Arg(args, "--variant") is { } v ? Math.Max(0, Array.FindIndex(Variants.All, x => x.Key.Equals(v, StringComparison.OrdinalIgnoreCase))) : 0;
        var state = int.TryParse(Arg(args, "--state"), out var st) ? Math.Clamp(st - 1, 0, Data.Scenarios.Length - 1) : 0;
        app.Run(new PrototypeWindow(variant, state));
    }

    static string? Arg(string[] args, string name) => args.SkipWhile(a => a != name).Skip(1).FirstOrDefault();
}

class PrototypeWindow : Window
{
    int _variant, _state;
    readonly ContentControl _content = new();
    readonly TextBlock _variantLabel = new() { Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, MinWidth = 300, TextAlignment = TextAlignment.Center };
    readonly TextBlock _stateLabel = new() { Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, MinWidth = 300, TextAlignment = TextAlignment.Center };

    public PrototypeWindow(int variant, int state)
    {
        _variant = variant;
        _state = state;
        MinWidth = 380;
        MinHeight = 400;
        var root = new Grid();
        root.Children.Add(_content);
        root.Children.Add(Switcher());
        Content = root;
        PreviewKeyDown += OnKey;
        Show(resize: true);
    }

    // Плашка нарочно не в Fluent-стиле: она не часть окна, которое оцениваем
    UIElement Switcher()
    {
        var p = new StackPanel();
        p.Children.Add(Row(_variantLabel, () => Step(ref _variant, -1, Variants.All.Length, true), () => Step(ref _variant, 1, Variants.All.Length, true)));
        p.Children.Add(Row(_stateLabel, () => Step(ref _state, -1, Data.Scenarios.Length, false), () => Step(ref _state, 1, Data.Scenarios.Length, false)));
        return new Border
        {
            Child = p,
            Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x10, 0x10, 0x10)),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(6, 3, 6, 3),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 8),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 12, Opacity = 0.4, ShadowDepth = 2 },
        };
    }

    static UIElement Row(TextBlock label, Action prev, Action next)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal };
        p.Children.Add(Arrow("‹", prev));
        p.Children.Add(label);
        p.Children.Add(Arrow("›", next));
        return p;
    }

    static Button Arrow(string s, Action a)
    {
        var b = new Button { Content = s, Width = 28, Height = 24, FontSize = 16, Padding = new Thickness(0), Foreground = Brushes.White, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Focusable = false };
        b.Click += (_, _) => a();
        return b;
    }

    void Step(ref int i, int d, int n, bool resize)
    {
        i = (i + d + n) % n;
        Show(resize);
    }

    void Show(bool resize)
    {
        var v = Variants.All[_variant];
        var s = Data.Scenarios[_state];
        _content.Content = v.Build(s);
        _variantLabel.Text = $"Вариант {v.Key}: {v.Name}";
        _stateLabel.Text = $"Состояние {_state + 1}/{Data.Scenarios.Length}: {s.Title}";
        Title = $"WiFiControl — ПРОТОТИП {v.Key}";
        if (resize) { Width = v.Width; Height = v.Height; }
    }

    void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Left or Key.Right) || Keyboard.FocusedElement is TextBox or ComboBox or ComboBoxItem) return;
        var d = e.Key == Key.Left ? -1 : 1;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) Step(ref _state, d, Data.Scenarios.Length, false);
        else Step(ref _variant, d, Variants.All.Length, true);
        e.Handled = true;
    }
}
