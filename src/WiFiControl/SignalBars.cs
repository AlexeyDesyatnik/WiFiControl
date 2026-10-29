using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;

namespace WiFiControl;

/// <summary>
/// Четыре полоски сигнала. Рисуются сами: глифы шрифта отличаются между Windows 10 и 11.
/// </summary>
internal sealed class SignalBars : StackPanel
{
    public static readonly DependencyProperty SignalProperty = DependencyProperty.Register(
        nameof(Signal), typeof(int), typeof(SignalBars), new PropertyMetadata(0, (bars, _) => ((SignalBars)bars).Draw()));

    public static readonly DependencyProperty ScaleProperty = DependencyProperty.Register(
        nameof(Scale), typeof(double), typeof(SignalBars), new PropertyMetadata(1.0, (bars, _) => ((SignalBars)bars).Draw()));

    public SignalBars()
    {
        Orientation = Orientation.Horizontal;
        VerticalAlignment = VerticalAlignment.Center;
        Draw();
    }

    /// <summary>Качество сигнала, 0–100.</summary>
    public int Signal
    {
        get => (int)GetValue(SignalProperty);
        set => SetValue(SignalProperty, value);
    }

    public double Scale
    {
        get => (double)GetValue(ScaleProperty);
        set => SetValue(ScaleProperty, value);
    }

    /// <summary>Сколько полосок горит: по одной на каждую четверть, хотя бы одна у видимой сети.</summary>
    private static int Count(int signal) => Math.Clamp((signal + 24) / 25, 1, 4);

    private void Draw()
    {
        Children.Clear();
        int lit = Count(Signal);
        ToolTip = "Сигнал: " + lit + " из 4";
        for (int i = 0; i < 4; i++)
        {
            var bar = new Rectangle
            {
                Width = 3 * Scale,
                Height = (4 + 3 * i) * Scale,
                Margin = new Thickness(0, 0, 1.5 * Scale, 0),
                VerticalAlignment = VerticalAlignment.Bottom,
                RadiusX = 1,
                RadiusY = 1,
                Opacity = i < lit ? 1 : 0.35,
            };
            bar.SetResourceReference(Shape.FillProperty, i < lit ? "TextFillColorPrimaryBrush" : "ControlStrongStrokeColorDisabledBrush");
            Children.Add(bar);
        }
    }
}
