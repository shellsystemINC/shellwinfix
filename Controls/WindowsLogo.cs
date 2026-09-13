using System.Windows;
using System.Windows.Media;
using TaskbarTYOL.Services;

namespace TaskbarTYOL.Controls;

/// <summary>Four-pane Windows-style logo drawn from theme brushes (LogoBrush1..4 + LogoStyle).</summary>
public sealed class WindowsLogo : FrameworkElement
{
    public WindowsLogo()
    {
        ThemeManager.ThemeChanged += OnThemeChanged;
        Unloaded += (_, _) => ThemeManager.ThemeChanged -= OnThemeChanged;
    }

    private void OnThemeChanged(string _) => Dispatcher.BeginInvoke(InvalidateVisual);

    private Brush B(string key) => TryFindResource(key) as Brush ?? Brushes.White;

    protected override Size MeasureOverride(Size availableSize)
    {
        double s = Math.Min(double.IsInfinity(availableSize.Width) ? 20 : availableSize.Width,
                            double.IsInfinity(availableSize.Height) ? 20 : availableSize.Height);
        return new Size(s, s);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        string style = TryFindResource("LogoStyle") as string ?? "Fluent";
        Brush[] brushes = { B("LogoBrush1"), B("LogoBrush2"), B("LogoBrush3"), B("LogoBrush4") };

        if (style is "Fluent" or "Metro")
        {
            double gap = Math.Max(1, w * 0.09);
            double cell = (w - gap) / 2;
            DrawGrid(dc, brushes, 0, 0, cell, gap, style == "Fluent" ? cell * 0.08 : 0);
            return;
        }

        // Waving-flag variants (Aero / Luna / Classic)
        double left = 0;
        if (style == "Classic")
        {
            // Win95-style "speed lines" on the left
            var pen = new Pen(B("TaskbarForegroundMuted"), Math.Max(1, h * 0.06));
            for (int i = 0; i < 3; i++)
            {
                double y = h * (0.35 + 0.15 * i);
                dc.DrawLine(pen, new Point(0, y), new Point(w * 0.18, y));
            }
            left = w * 0.24;
        }

        double fw = w - left, fh = h;
        double g = Math.Max(1, fw * 0.07);
        double c = (fw - g) / 2;
        dc.PushTransform(new SkewTransform(0, -10, left + fw / 2, fh / 2));
        dc.PushTransform(new ScaleTransform(1, 0.85, left + fw / 2, fh / 2));
        DrawGrid(dc, brushes, left, 0, c, g, c * 0.12);
        dc.Pop();
        dc.Pop();
    }

    private static void DrawGrid(DrawingContext dc, Brush[] b, double x, double y, double cell, double gap, double radius)
    {
        dc.DrawRoundedRectangle(b[0], null, new Rect(x, y, cell, cell), radius, radius);
        dc.DrawRoundedRectangle(b[1], null, new Rect(x + cell + gap, y, cell, cell), radius, radius);
        dc.DrawRoundedRectangle(b[2], null, new Rect(x, y + cell + gap, cell, cell), radius, radius);
        dc.DrawRoundedRectangle(b[3], null, new Rect(x + cell + gap, y + cell + gap, cell, cell), radius, radius);
    }
}
