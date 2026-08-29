using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using LeaseSimulation.App.ViewModels;
using LeaseSimulation.App.Views;

namespace LeaseSimulation.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new Window
            {
                Title = "Windows Fabric Lease Lab",
                Width = 1280,
                Height = 800,
                MinWidth = 1050,
                MinHeight = 680,
                Content = CreateMainView(),
            };
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            singleView.MainView = CreateMainView();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static MainView CreateMainView() => new()
    {
        DataContext = new MainViewModel(),
    };
}