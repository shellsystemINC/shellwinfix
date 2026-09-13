using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace TaskbarTYOL.Controls;

/// <summary>A small, fully theme-driven month view (the stock WPF Calendar hard-codes a white look).</summary>
public partial class MonthCalendar : UserControl
{
    public sealed record DayCell(DateTime Date, int Day, bool IsCurrentMonth, bool IsToday, bool IsWeekend);

    private DateTime _month = new(DateTime.Today.Year, DateTime.Today.Month, 1);

    public MonthCalendar()
    {
        InitializeComponent();
        BuildWeekdays();
        Render();
    }

    /// <summary>Jump back to the current month (call this every time the popup opens).</summary>
    public void GoToday()
    {
        _month = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        Render();
    }

    private static DayOfWeek FirstDay => CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;

    private void BuildWeekdays()
    {
        var names = CultureInfo.CurrentCulture.DateTimeFormat.ShortestDayNames;
        var list = new List<string>();
        for (int i = 0; i < 7; i++) list.Add(names[((int)FirstDay + i) % 7]);
        WeekdayRow.ItemsSource = list;
    }

    private void Render()
    {
        MonthText.Text = _month.ToString("MMMM yyyy", CultureInfo.CurrentCulture);

        int lead = ((int)_month.DayOfWeek - (int)FirstDay + 7) % 7;
        var start = _month.AddDays(-lead);
        var today = DateTime.Today;
        var cells = new List<DayCell>(42);
        for (int i = 0; i < 42; i++)
        {
            var d = start.AddDays(i);
            cells.Add(new DayCell(d, d.Day, d.Month == _month.Month, d == today,
                                  d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday));
        }
        DayGrid.ItemsSource = cells;
    }

    private void Prev_Click(object sender, RoutedEventArgs e) { _month = _month.AddMonths(-1); Render(); }
    private void Next_Click(object sender, RoutedEventArgs e) { _month = _month.AddMonths(1); Render(); }
    private void Today_Click(object sender, RoutedEventArgs e) => GoToday();
}
