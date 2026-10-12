using System.Collections.Immutable;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Sponde.Application;
using Sponde.Domain;
using Sponde.Infrastructure;
using Xunit;

namespace Sponde.Tests;

public sealed class UiAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
public sealed class OverviewTests
{
    [Fact]
    public async Task OverviewContainsReferenceMetricsAndRendersAtDesktopSize()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(UiAppBuilder));
        await session.Dispatch(async () =>
        {
            var root = Path.Combine(Path.GetTempPath(), "sponde-ui-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                using var store = new SqliteFinanceStore(root);
                var evidence = Path.Combine(root, "demo.pdf"); File.WriteAllText(evidence, "synthetic");
                var rows = Enumerable.Range(4, 6).SelectMany(m => new[]
                {
                    new Transaction(Guid.NewGuid(), new(2000,m,5), "N26", "Synthetic employer", new(300000), MovementKind.Income, "Gehalt Netto", ReviewStatus.Assigned, "", "demo.pdf", 1, "", null, ""),
                    new Transaction(Guid.NewGuid(), new(2000,m,6), "N26", "Synthetic landlord", new(-120000), MovementKind.Expense, "Miete", ReviewStatus.Assigned, "", "demo.pdf", 1, "", null, ""),
                    new Transaction(Guid.NewGuid(), new(2000,m,7), "N26", "Synthetic shop", new(-25000), MovementKind.Expense, "Lebensmittel", ReviewStatus.Review, "", "demo.pdf", 1, "", null, "")
                }).ToImmutableArray();
                store.Commit(new("Demo", new(rows, [], [new("Miete", new(120000), "N26", false)], []), [new(evidence, Evidence.Hash(evidence))], [], "pdf-demo-v1"));
                var window = new MainWindow(new FinanceService(store, new StatementReader()));
                window.Show(); await Task.Delay(250);
                var labels = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToArray();
                Assert.Contains("Dein Budget", labels); Assert.Contains("Einnahmen", labels); Assert.Contains("Sparen netto", labels);
                Assert.Contains("Monatlicher Überschuss", labels); Assert.Contains("Überschuss kumuliert", labels);
                using var image = window.CaptureRenderedFrame(); Assert.NotNull(image);
                var output = Environment.GetEnvironmentVariable("SPONDE_UI_ARTIFACTS");
                if (!string.IsNullOrEmpty(output)) { Directory.CreateDirectory(output); image.Save(Path.Combine(output, "overview.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions()); }
                foreach (var name in new[] { "Buchungen", "Import & Prüfung", "Kontoabgleich", "Budget", "Regeln", "Entgelt", "Sicherung" })
                {
                    window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await Task.Delay(30);
                    Assert.NotEmpty(window.GetVisualDescendants().OfType<TextBlock>());
                }
                window.Close();
            }
            finally { Directory.Delete(root, true); }
        }, CancellationToken.None);
    }
}

