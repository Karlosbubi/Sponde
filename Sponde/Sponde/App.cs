using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Sponde.Application;
using Sponde.Infrastructure;

namespace Sponde;

public sealed class App : Avalonia.Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var args = desktop.Args ?? [];
            var index = Array.IndexOf(args, "--data-dir");
            var directory = index >= 0 && index + 1 < args.Length ? args[index + 1]
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sponde");
            var store = new SqliteFinanceStore(directory);
            desktop.MainWindow = new MainWindow(new FinanceService(store, new StatementReader()));
            desktop.Exit += (_, _) => store.Dispose();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
