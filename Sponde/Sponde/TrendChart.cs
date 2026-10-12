using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Sponde.Domain;

namespace Sponde;

public sealed class TrendChart : Control
{
    public IReadOnlyList<MonthlySummary> Months { get; init; } = [];
    public bool Cumulative { get; init; }
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Months.Count == 0) return;
        var width = Math.Max(100, Bounds.Width - 70);
        var height = Math.Max(80, Bounds.Height - 55);
        long running = 0;
        var points = Months.Select(m => { running += m.Surplus; return Cumulative ? running : m.Surplus; }).ToArray();
        var maximum = Math.Max(1, points.Append(0).Max());
        var minimum = Math.Min(0, points.Min());
        var range = Math.Max(1, maximum - minimum);
        double Y(long amount) => 15 + height * (maximum - amount) / range;
        var zero = Y(0);
        context.DrawLine(new Pen(Brushes.LightGray), new(55, zero), new(55 + width, zero));
        for (var i = 0; i < points.Length; i++)
        {
            var step = width / points.Length;
            var x = 55 + i * step + step * 0.2;
            var y = Y(points[i]);
            context.DrawRectangle(new SolidColorBrush(Color.Parse(points[i] >= 0 ? "#12856A" : "#D65A4A")), null,
                new Rect(x, Math.Min(y, zero), step * 0.6, Math.Max(2, Math.Abs(y - zero))));
            Draw(context, Months[i].Month.ToString("MMM", Presentation.German), x, height + 25, 11);
            Draw(context, (points[i] / 100m).ToString("N0", Presentation.German), x, points[i] >= 0 ? Math.Max(0, y - 18) : y + 2, 10);
        }
    }
    private static void Draw(DrawingContext context, string text, double x, double y, double size) => context.DrawText(
        new FormattedText(text, Presentation.German, FlowDirection.LeftToRight, Typeface.Default, size, new SolidColorBrush(Color.Parse("#526077"))), new(x, y));
}
