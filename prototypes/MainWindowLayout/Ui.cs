// ПРОТОТИП (#31): мелкие кирпичики, общие для вариантов. Раскладку каждый вариант строит сам.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace MainWindowLayout;

static class Ui
{
    public static readonly FontFamily Icons = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    public static T Res<T>(this T e, DependencyProperty p, string key) where T : FrameworkElement
    {
        e.SetResourceReference(p, key);
        return e;
    }

    public static TextBlock Text(string s, double size = 14, FontWeight? weight = null, bool secondary = false, bool wrap = false)
    {
        var t = new TextBlock { Text = s, FontSize = size, FontWeight = weight ?? FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center };
        t.TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap;
        if (!wrap) t.TextTrimming = TextTrimming.CharacterEllipsis;
        if (secondary) t.Res(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        return t;
    }

    public static TextBlock Glyph(string g, double size = 14, string? brush = null)
    {
        var t = new TextBlock { Text = g, FontFamily = Icons, FontSize = size, VerticalAlignment = VerticalAlignment.Center };
        if (brush != null) t.Res(TextBlock.ForegroundProperty, brush);
        return t;
    }

    public static Border Card(UIElement child, double padding = 16) =>
        new Border { Child = child, Padding = new Thickness(padding), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1) }
            .Res(Border.BackgroundProperty, "CardBackgroundFillColorDefaultBrush")
            .Res(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");

    // Полоски сигнала рисуются сами: глифы шрифта отличаются между Windows 10 и 11
    public static UIElement Bars(int n, double scale = 1)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, ToolTip = $"Сигнал: {n} из 4" };
        for (var i = 0; i < 4; i++)
        {
            var r = new Rectangle { Width = 3 * scale, Height = (4 + 3 * i) * scale, Margin = new Thickness(0, 0, 1.5 * scale, 0), VerticalAlignment = VerticalAlignment.Bottom, RadiusX = 1, RadiusY = 1 };
            r.SetResourceReference(Shape.FillProperty, i < n ? "TextFillColorPrimaryBrush" : "ControlStrongStrokeColorDisabledBrush");
            if (i >= n) r.Opacity = 0.35;
            p.Children.Add(r);
        }
        return p;
    }

    public static TextBlock Lock(Net n) =>
        n.Secured ? WithTip(Glyph("", 12, "TextFillColorSecondaryBrush"), n.Security) : WithTip(Glyph("", 12, "TextFillColorTertiaryBrush"), "Открытая сеть");

    static TextBlock WithTip(TextBlock t, string tip) { t.ToolTip = tip; return t; }

    public static string ProfileLine(Net n) =>
        n.Profile == "?" ? "профиль неизвестен" : n.Bound ? n.Profile : "по умолчанию: " + n.Profile;

    public static Button Button(string s, bool accent = false, bool enabled = true)
    {
        var b = new Button { Content = s, IsEnabled = enabled, Padding = new Thickness(12, 5, 12, 6) };
        if (accent) b.SetResourceReference(FrameworkElement.StyleProperty, "AccentButtonStyle");
        return b;
    }

    public static Button IconButton(string glyph, string tip, bool enabled = true) =>
        new() { Content = Glyph(glyph, 14), ToolTip = tip, IsEnabled = enabled, Padding = new Thickness(8, 6, 8, 6) };

    public static ComboBox ProfileCombo(Net n, bool enabled, double width = 190)
    {
        var c = new ComboBox { Width = width, IsEnabled = enabled, VerticalAlignment = VerticalAlignment.Center };
        if (n.Profile == "?")
        {
            c.Items.Add("профиль неизвестен");
            c.SelectedIndex = 0;
            return c;
        }
        foreach (var p in Data.Profiles)
            c.Items.Add(p == "Без привязки" && !n.Bound ? $"Без привязки (по умолчанию: {DefaultName(n)})" : p);
        c.SelectedIndex = n.Bound ? Array.IndexOf(Data.Profiles, n.Profile) : 0;
        c.ToolTip = "Профиль для этой сети";
        return c;
    }

    static string DefaultName(Net n) => n.Bound || n.Profile == "?" ? "Автоматически" : n.Profile;

    public static CheckBox NoAuto(Net n, bool enabled) =>
        new() { Content = "Не подключаться автоматически", IsChecked = n.NoAutoConnect, IsEnabled = enabled, VerticalAlignment = VerticalAlignment.Center };

    public static ComboBox AdapterCombo(Scenario s, double width = 230)
    {
        var c = new ComboBox { Width = width, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Wi-Fi-адаптер" };
        foreach (var a in s.Adapters) c.Items.Add(a);
        c.SelectedIndex = 0;
        return c;
    }

    public static (string brush, string glyph, string fg) Look(Severity s) => s switch
    {
        Severity.Error => ("SystemFillColorCriticalBackgroundBrush", "", "SystemFillColorCriticalBrush"),
        Severity.Warning => ("SystemFillColorCautionBackgroundBrush", "", "SystemFillColorCautionBrush"),
        _ => ("SystemFillColorAttentionBackgroundBrush", "", "SystemFillColorAttentionBrush"),
    };

    // Полоса в духе InfoBar: значок, текст, необязательная кнопка «Применить»
    public static Border InfoBar(Problem p, bool applyEnabled = true, bool compact = false)
    {
        var (bg, glyph, fg) = Look(p.Severity);
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = Glyph(glyph, compact ? 14 : 16, fg);
        icon.VerticalAlignment = VerticalAlignment.Top;
        icon.Margin = new Thickness(0, 2, compact ? 8 : 10, 0);
        g.Children.Add(icon);
        var texts = new StackPanel();
        texts.Children.Add(Text(p.Text, compact ? 13 : 14, FontWeights.SemiBold, wrap: true));
        if (p.Detail != null) texts.Children.Add(Text(p.Detail, compact ? 11.5 : 12, wrap: true));
        Grid.SetColumn(texts, 1);
        g.Children.Add(texts);
        if (p.Apply)
        {
            var b = Button("Применить", accent: true, enabled: applyEnabled);
            if (compact) b.Padding = new Thickness(10, 3, 10, 4);
            b.Margin = new Thickness(compact ? 8 : 12, 0, 0, 0);
            b.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(b, 2);
            g.Children.Add(b);
        }
        return new Border { Child = g, Padding = compact ? new Thickness(10, 6, 8, 6) : new Thickness(12, 10, 12, 10), CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Margin = new Thickness(0, compact ? 4 : 6, 0, 0) }
            .Res(Border.BackgroundProperty, bg)
            .Res(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");
    }

    public static Border FileBanner(bool compact = false)
    {
        var b = InfoBar(new Problem(Severity.Error, "Файл профилей не прочитан", Data.FileError), compact: compact);
        return AddButtons(b, (Grid)b.Child, "Открыть папку", "Перечитать");
    }

    static Border AddButtons(Border b, Grid g, params string[] names)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        for (var i = 0; i < names.Length; i++)
        {
            var btn = Button(names[i], accent: i == names.Length - 1);
            btn.Margin = new Thickness(0, 0, 8, 0);
            p.Children.Add(btn);
        }
        ((StackPanel)g.Children[1]).Children.Add(p);
        return b;
    }

    // Чем заменить список сетей в особых состояниях; null — список показывается
    public static UIElement? SpecialListArea(Scenario s) => s.State switch
    {
        ListState.NoLocation => Explanation("", "Нет доступа к местоположению",
            "Windows 11 24H2 и новее показывает список Wi-Fi-сетей только программам с доступом к местоположению. Без него WiFiControl не видит сети и не может переключаться. Блок «Сейчас» и профили работают.",
            "Проверить снова", "Открыть параметры"),
        ListState.NoAdapter => Explanation("", "Wi-Fi-адаптер не найден",
            "Подключите USB-адаптер или включите адаптер в диспетчере устройств. Профили и привязки можно настраивать и без него.",
            "Обновить"),
        ListState.RadioOff => Explanation("", "Wi-Fi выключен",
            "Включите Wi-Fi в быстрых настройках Windows или кнопкой на ноутбуке.",
            "Обновить"),
        _ => null,
    };

    static UIElement Explanation(string glyph, string title, string text, params string[] buttons)
    {
        var p = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 420, Margin = new Thickness(16, 32, 16, 32) };
        var icon = Glyph(glyph, 40, "TextFillColorSecondaryBrush");
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        p.Children.Add(icon);
        var t = Text(title, 18, FontWeights.SemiBold);
        t.HorizontalAlignment = HorizontalAlignment.Center;
        t.Margin = new Thickness(0, 12, 0, 6);
        p.Children.Add(t);
        var d = Text(text, 14, secondary: true, wrap: true);
        d.TextAlignment = TextAlignment.Center;
        p.Children.Add(d);
        var bp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 16, 0, 0) };
        for (var i = 0; i < buttons.Length; i++)
        {
            var b = Button(buttons[i], accent: i == buttons.Length - 1);
            b.Margin = new Thickness(4, 0, 4, 0);
            bp.Children.Add(b);
        }
        p.Children.Add(bp);
        return p;
    }
}
