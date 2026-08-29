using System;
using Avalonia;
using LeaseApp = LeaseSimulation.App.App;

namespace LeaseSimulation.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<LeaseApp>()
        .UsePlatformDetect()
        .WithInterFont()
        .LogToTrace();
}