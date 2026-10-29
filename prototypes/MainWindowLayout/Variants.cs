// ПРОТОТИП (#31): три раскладки главного окна. Каждая строит окно целиком из одного сценария.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using static MainWindowLayout.Ui;

namespace MainWindowLayout;

record Variant(string Key, string Name, double Width, double Height, Func<Scenario, UIElement> Build);

static class Variants
{
    public static readonly Variant[] All =
    [
        new("A", "Карточка сверху, строки раскрываются", 600, 780, BuildA),
        new("B", "Узкое, как меню Wi-Fi в Windows", 400, 780, BuildB),
        new("C", "Таблица и панель подробностей", 1000, 680, BuildC),
    ];

    // ---------- A: карточка «Сейчас», полосы проблем, список с раскрывающимися действиями ----------

    static UIElement BuildA(Scenario s)
    {
        var root = new DockPanel { Margin = new Thickness(20, 16, 20, 72) };

        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var tools = new StackPanel { Orientation = Orientation.Horizontal };
        if (s.Adapters.Length > 1) { var a = AdapterCombo(s, 210); a.Margin = new Thickness(0, 0, 8, 0); tools.Children.Add(a); }
        var refresh = IconButton("", "Обновить");
        refresh.Margin = new Thickness(0, 0, 8, 0);
        tools.Children.Add(refresh);
        tools.Children.Add(Button("Профили и привязки…", enabled: s.CanEdit));
        DockPanel.SetDock(tools, Dock.Right);
        header.Children.Add(tools);
        header.Children.Add(Text("WiFiControl", 20, FontWeights.SemiBold));
        Top(root, header);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var rows = new (string, UIElement)[]
        {
            ("Сеть", NetworkLine(s)),
            ("Адрес", Text(s.Address)),
            ("Шлюз", Text(s.Gateway)),
            ("DNS", Text(s.Dns)),
            ("Прокси", Text(s.Proxy)),
        };
        for (var i = 0; i < rows.Length; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var k = Text(rows[i].Item1, 14, secondary: true);
            k.Margin = new Thickness(0, 3, 0, 3);
            Grid.SetRow(k, i);
            grid.Children.Add(k);
            Grid.SetRow(rows[i].Item2, i);
            Grid.SetColumn(rows[i].Item2, 1);
            grid.Children.Add(rows[i].Item2);
        }
        var card = new StackPanel();
        var title = Text("Сейчас", 13, FontWeights.SemiBold, secondary: true);
        title.Margin = new Thickness(0, 0, 0, 6);
        card.Children.Add(title);
        card.Children.Add(grid);
        Top(root, Card(card));

        foreach (var p in s.Problems) Top(root, InfoBar(p));
        if (s.State == ListState.FileNotRead) Top(root, FileBanner());

        var listTitle = Text("Сети рядом", 16, FontWeights.SemiBold);
        listTitle.Margin = new Thickness(0, 18, 0, 6);
        Top(root, listTitle);

        if (SpecialListArea(s) is { } special) { root.Children.Add(Card(special)); return root; }

        var list = new ListBox { BorderThickness = new Thickness(0), Background = Brushes.Transparent, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        var actionPanels = new List<UIElement>();
        foreach (var n in s.Nets)
        {
            var item = new StackPanel { Margin = new Thickness(4, 6, 4, 6) };
            var line = new DockPanel();
            var bars = Bars(n.Bars, 1.2);
            ((FrameworkElement)bars).Margin = new Thickness(0, 0, 14, 0);
            DockPanel.SetDock(bars, Dock.Left);
            line.Children.Add(bars);
            var lk = Lock(n);
            DockPanel.SetDock(lk, Dock.Right);
            line.Children.Add(lk);
            var names = new StackPanel();
            var nameRow = new StackPanel { Orientation = Orientation.Horizontal };
            nameRow.Children.Add(Text(n.Name, 14, n.Current ? FontWeights.SemiBold : FontWeights.Normal));
            if (n.Current) nameRow.Children.Add(Badge("Подключено"));
            names.Children.Add(nameRow);
            names.Children.Add(Text(ProfileLine(n), 12, secondary: true));
            line.Children.Add(names);
            item.Children.Add(line);

            var actions = new WrapPanel { Margin = new Thickness(30, 10, 0, 4), Visibility = Visibility.Collapsed };
            var main = n.Current ? Button("Применить профиль…", enabled: s.CanSwitch) : Button("Подключиться", accent: true, enabled: s.CanSwitch);
            main.Margin = new Thickness(0, 0, 8, 6);
            actions.Children.Add(main);
            var combo = ProfileCombo(n, s.CanBind);
            combo.Margin = new Thickness(0, 0, 12, 6);
            actions.Children.Add(combo);
            if (n.HasWindowsProfile) actions.Children.Add(NoAuto(n, s.CanSwitch));
            item.Children.Add(actions);
            actionPanels.Add(actions);
            list.Items.Add(item);
        }
        list.Items.Add(HiddenRow());
        actionPanels.Add(new UIElement());
        list.SelectionChanged += (_, _) =>
        {
            for (var i = 0; i < actionPanels.Count; i++)
                actionPanels[i].Visibility = i == list.SelectedIndex ? Visibility.Visible : Visibility.Collapsed;
        };
        list.SelectedIndex = Math.Min(1, s.Nets.Length - 1);
        root.Children.Add(list);
        return root;
    }

    static UIElement NetworkLine(Scenario s)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal };
        if (s.CurrentNet is { } n) { var b = Bars(n.Bars); ((FrameworkElement)b).Margin = new Thickness(0, 0, 8, 0); p.Children.Add(b); }
        p.Children.Add(Text(s.Network, 14, FontWeights.SemiBold));
        var prof = Text("   профиль: " + s.NetworkProfile, 14, secondary: true);
        if (s.NetworkProfile != "—") p.Children.Add(prof);
        return p;
    }

    static Border Badge(string s) =>
        new Border { Child = Text(s, 11), Padding = new Thickness(6, 1, 6, 2), CornerRadius = new CornerRadius(4), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center }
            .Res(Border.BackgroundProperty, "SystemFillColorSuccessBackgroundBrush");

    static UIElement HiddenRow()
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 8, 4, 8) };
        var g = Glyph("", 14, "TextFillColorSecondaryBrush");
        g.Margin = new Thickness(2, 0, 16, 0);
        p.Children.Add(g);
        p.Children.Add(Text("Скрытая сеть…", 14));
        return p;
    }

    static void Top(DockPanel root, UIElement e)
    {
        DockPanel.SetDock(e, Dock.Top);
        root.Children.Add(e);
    }

    // ---------- B: узкое окно, всё действие в строке сразу, служебное — в подвале ----------

    static UIElement BuildB(Scenario s)
    {
        var root = new DockPanel();

        var footer = new DockPanel { Margin = new Thickness(12, 8, 12, 64) };
        footer.Res(Panel.BackgroundProperty, "LayerFillColorDefaultBrush");
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        right.Children.Add(IconButton("", "Обновить"));
        var prof = IconButton("", "Профили и привязки…", s.CanEdit);
        prof.Margin = new Thickness(6, 0, 0, 0);
        right.Children.Add(prof);
        DockPanel.SetDock(right, Dock.Right);
        footer.Children.Add(right);
        if (s.Adapters.Length > 1) footer.Children.Add(AdapterCombo(s, 220));
        else footer.Children.Add(Text(s.Adapters.FirstOrDefault() ?? "", 12, secondary: true));
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        var head = new StackPanel { Margin = new Thickness(16, 16, 16, 8) };
        var nameRow = new StackPanel { Orientation = Orientation.Horizontal };
        if (s.CurrentNet is { } cur) { var b = Bars(cur.Bars, 1.6); ((FrameworkElement)b).Margin = new Thickness(0, 0, 12, 0); nameRow.Children.Add(b); }
        nameRow.Children.Add(Text(s.State switch
        {
            ListState.NoAdapter => "Wi-Fi-адаптер не найден",
            ListState.RadioOff => "Wi-Fi выключен",
            ListState.NoLocation => "Сеть неизвестна",
            _ => s.Network,
        }, 22, FontWeights.SemiBold));
        head.Children.Add(nameRow);
        if (s.NetworkProfile != "—") head.Children.Add(Text("Подключено · " + s.NetworkProfile, 13, secondary: true));
        var details = Text($"{s.Address}\nшлюз {s.Gateway} · DNS {s.Dns}\nпрокси: {s.Proxy}", 12, secondary: true, wrap: true);
        details.Margin = new Thickness(0, 6, 0, 0);
        head.Children.Add(details);
        foreach (var p in s.Problems) head.Children.Add(CompactProblem(p));
        if (s.State == ListState.FileNotRead) head.Children.Add(FileBanner());
        DockPanel.SetDock(head, Dock.Top);
        root.Children.Add(head);

        var sep = new Border { Height = 1, Margin = new Thickness(0, 4, 0, 0) }.Res(Border.BackgroundProperty, "DividerStrokeColorDefaultBrush");
        DockPanel.SetDock(sep, Dock.Top);
        root.Children.Add(sep);

        if (SpecialListArea(s) is { } special) { root.Children.Add(special); return root; }

        var rows = new StackPanel();
        foreach (var n in s.Nets)
        {
            var r = new StackPanel { Margin = new Thickness(16, 10, 16, 10) };
            var line = new DockPanel();
            var bars = Bars(n.Bars, 1.2);
            ((FrameworkElement)bars).Margin = new Thickness(0, 0, 12, 0);
            DockPanel.SetDock(bars, Dock.Left);
            line.Children.Add(bars);
            var lk = Lock(n);
            DockPanel.SetDock(lk, Dock.Right);
            line.Children.Add(lk);
            line.Children.Add(Text(n.Name, 14, n.Current ? FontWeights.SemiBold : FontWeights.Normal));
            r.Children.Add(line);

            var act = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
            var main = n.Current ? Button("Применить…", enabled: s.CanSwitch) : Button("Подключиться", accent: true, enabled: s.CanSwitch);
            main.Margin = new Thickness(8, 0, 0, 0);
            DockPanel.SetDock(main, Dock.Right);
            act.Children.Add(main);
            var combo = ProfileCombo(n, s.CanBind, double.NaN);
            act.Children.Add(combo);
            r.Children.Add(act);
            if (n.HasWindowsProfile) { var c = NoAuto(n, s.CanSwitch); c.Margin = new Thickness(0, 4, 0, 0); c.FontSize = 12; r.Children.Add(c); }

            var border = new Border { Child = r, BorderThickness = new Thickness(0, 0, 0, 1) }.Res(Border.BorderBrushProperty, "DividerStrokeColorDefaultBrush");
            if (n.Current) border.Res(Border.BackgroundProperty, "SubtleFillColorSecondaryBrush");
            rows.Children.Add(border);
        }
        var hidden = new Button { Content = "Скрытая сеть…", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(16, 10, 16, 16), IsEnabled = s.CanSwitch };
        rows.Children.Add(hidden);
        root.Children.Add(new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        return root;
    }

    static UIElement CompactProblem(Problem p)
    {
        var (_, glyph, fg) = Look(p.Severity);
        var d = new DockPanel { Margin = new Thickness(0, 8, 0, 0), ToolTip = p.Detail };
        var g = Glyph(glyph, 14, fg);
        g.Margin = new Thickness(0, 0, 8, 0);
        g.VerticalAlignment = VerticalAlignment.Top;
        DockPanel.SetDock(g, Dock.Left);
        d.Children.Add(g);
        if (p.Apply)
        {
            var b = Button("Применить", accent: true);
            b.Padding = new Thickness(10, 3, 10, 4);
            b.Margin = new Thickness(8, 0, 0, 0);
            DockPanel.SetDock(b, Dock.Right);
            d.Children.Add(b);
        }
        d.Children.Add(Text(p.Text, 13, wrap: true));
        return d;
    }

    // ---------- C: «Сейчас» плитками в полосу, таблица сетей слева, подробности выбранной справа ----------

    static UIElement BuildC(Scenario s)
    {
        var root = new DockPanel { Margin = new Thickness(20, 16, 20, 72) };

        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var tools = new StackPanel { Orientation = Orientation.Horizontal };
        if (s.Adapters.Length > 1)
        {
            var l = Text("Адаптер:", 14, secondary: true);
            l.Margin = new Thickness(0, 0, 8, 0);
            tools.Children.Add(l);
            var a = AdapterCombo(s);
            a.Margin = new Thickness(0, 0, 16, 0);
            tools.Children.Add(a);
        }
        var refresh = Button("Обновить");
        refresh.Margin = new Thickness(0, 0, 8, 0);
        tools.Children.Add(refresh);
        tools.Children.Add(Button("Профили и привязки…", enabled: s.CanEdit));
        DockPanel.SetDock(tools, Dock.Right);
        header.Children.Add(tools);
        header.Children.Add(Text("WiFiControl", 20, FontWeights.SemiBold));
        Top(root, header);

        var tiles = new UniformGrid { Rows = 1 };
        tiles.Children.Add(Tile("Сеть", s.Network, s.NetworkProfile == "—" ? null : s.NetworkProfile));
        tiles.Children.Add(Tile("Адрес", s.Address));
        tiles.Children.Add(Tile("Шлюз", s.Gateway));
        tiles.Children.Add(Tile("DNS", s.Dns));
        tiles.Children.Add(Tile("Прокси", s.Proxy));
        Top(root, tiles);

        if (s.Problems.Length > 0)
        {
            var chips = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            foreach (var p in s.Problems) chips.Children.Add(Chip(p));
            Top(root, chips);
        }
        if (s.State == ListState.FileNotRead) Top(root, FileBanner());

        var main = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star), MinWidth = 300 });
        root.Children.Add(main);

        if (SpecialListArea(s) is { } special)
        {
            var c = Card(special);
            Grid.SetColumnSpan(c, 3);
            main.Children.Add(c);
            return root;
        }

        var left = new DockPanel();
        var cols = new[] { 36.0, double.NaN, 130, 190 };
        var head = TableRow(cols, Text("", 12), Text("Сеть", 12, secondary: true), Text("Защита", 12, secondary: true), Text("Профиль", 12, secondary: true));
        head.Margin = new Thickness(10, 0, 10, 4);
        DockPanel.SetDock(head, Dock.Top);
        left.Children.Add(head);
        var hidden = Button("Скрытая сеть…", enabled: s.CanSwitch);
        hidden.HorizontalAlignment = HorizontalAlignment.Left;
        hidden.Margin = new Thickness(0, 8, 0, 0);
        DockPanel.SetDock(hidden, Dock.Bottom);
        left.Children.Add(hidden);
        var list = new ListBox { HorizontalContentAlignment = HorizontalAlignment.Stretch };
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        foreach (var n in s.Nets)
        {
            var name = new StackPanel { Orientation = Orientation.Horizontal };
            name.Children.Add(Text(n.Name, 14, n.Current ? FontWeights.SemiBold : FontWeights.Normal));
            if (n.Current) name.Children.Add(Badge("Подключено"));
            var sec = new StackPanel { Orientation = Orientation.Horizontal };
            var lk = Lock(n);
            lk.Margin = new Thickness(0, 0, 6, 0);
            sec.Children.Add(lk);
            sec.Children.Add(Text(n.Security, 12, secondary: true));
            var r = TableRow(cols, Bars(n.Bars, 1.1), name, sec, Text(ProfileLine(n), 13, secondary: !n.Bound));
            r.Margin = new Thickness(0, 6, 0, 6);
            list.Items.Add(r);
        }
        left.Children.Add(list);
        main.Children.Add(left);

        var detail = new ContentControl();
        Grid.SetColumn(detail, 2);
        main.Children.Add(detail);
        list.SelectionChanged += (_, _) => detail.Content = list.SelectedIndex >= 0 ? Detail(s, s.Nets[list.SelectedIndex]) : null;
        list.SelectedIndex = Math.Min(1, s.Nets.Length - 1);
        return root;
    }

    static Grid TableRow(double[] widths, params UIElement[] cells)
    {
        var g = new Grid();
        for (var i = 0; i < widths.Length; i++)
        {
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = double.IsNaN(widths[i]) ? new GridLength(1, GridUnitType.Star) : new GridLength(widths[i]) });
            Grid.SetColumn(cells[i], i);
            g.Children.Add(cells[i]);
        }
        return g;
    }

    static UIElement Tile(string caption, string value, string? sub = null)
    {
        var p = new StackPanel();
        p.Children.Add(Text(caption, 12, secondary: true));
        var v = Text(value, 14, FontWeights.SemiBold);
        v.ToolTip = value;
        p.Children.Add(v);
        if (sub != null) p.Children.Add(Text(sub, 12, secondary: true));
        var c = Card(p, 12);
        c.Margin = new Thickness(0, 0, 8, 0);
        return c;
    }

    static UIElement Chip(Problem p)
    {
        var (bg, glyph, fg) = Look(p.Severity);
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        var g = Glyph(glyph, 13, fg);
        g.Margin = new Thickness(0, 0, 6, 0);
        sp.Children.Add(g);
        sp.Children.Add(Text(p.Text, 13));
        if (p.Apply)
        {
            var b = Button("Применить", accent: true);
            b.Padding = new Thickness(8, 2, 8, 3);
            b.Margin = new Thickness(10, 0, 0, 0);
            sp.Children.Add(b);
        }
        return new Border { Child = sp, Padding = new Thickness(10, 5, 6, 5), CornerRadius = new CornerRadius(14), Margin = new Thickness(0, 0, 6, 6), ToolTip = p.Detail, BorderThickness = new Thickness(1) }
            .Res(Border.BackgroundProperty, bg)
            .Res(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");
    }

    static UIElement Detail(Scenario s, Net n)
    {
        var p = new StackPanel();
        var top = new StackPanel { Orientation = Orientation.Horizontal };
        var b = Bars(n.Bars, 1.8);
        ((FrameworkElement)b).Margin = new Thickness(0, 0, 12, 0);
        top.Children.Add(b);
        var name = Text(n.Name, 20, FontWeights.SemiBold, wrap: true);
        name.MaxWidth = 280;
        top.Children.Add(name);
        p.Children.Add(top);
        p.Children.Add(Text(n.Current ? "Подключено" : "Не подключено", 13, secondary: true));

        var facts = Text($"Защита: {n.Security}\nПрофиль сети Windows: {(n.HasWindowsProfile ? "есть" : "нет, при подключении спросим пароль")}", 13, wrap: true);
        facts.Margin = new Thickness(0, 12, 0, 12);
        p.Children.Add(facts);

        p.Children.Add(Text("Профиль для сети", 12, secondary: true));
        var combo = ProfileCombo(n, s.CanBind, double.NaN);
        combo.Margin = new Thickness(0, 4, 0, 10);
        p.Children.Add(combo);
        if (n.HasWindowsProfile) p.Children.Add(NoAuto(n, s.CanSwitch));

        var main = n.Current ? Button("Применить профиль…", enabled: s.CanSwitch) : Button("Подключиться", accent: true, enabled: s.CanSwitch);
        main.HorizontalAlignment = HorizontalAlignment.Stretch;
        main.Margin = new Thickness(0, 16, 0, 0);
        p.Children.Add(main);
        return Card(p);
    }
}
