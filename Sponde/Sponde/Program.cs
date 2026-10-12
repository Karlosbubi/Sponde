using Avalonia;
namespace Sponde;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    public static Avalonia.AppBuilder BuildAvaloniaApp() => Avalonia.AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
