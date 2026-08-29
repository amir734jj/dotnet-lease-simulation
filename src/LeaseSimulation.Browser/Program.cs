using System.Threading.Tasks;
using Avalonia;
using Avalonia.Browser;
using LeaseApp = LeaseSimulation.App.App;

namespace LeaseSimulation.Browser;

internal static partial class Program
{
    private static Task Main(string[] args) => AppBuilder.Configure<LeaseApp>()
        .WithInterFont()
        .StartBrowserAppAsync("out");
}