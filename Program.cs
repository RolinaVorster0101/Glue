using System;
using Avalonia;
using Microsoft.Build.Locator;

namespace Glue;

internal static class Program
{
    // Avalonia's entry point convention — do not add async modifiers here.
    [STAThread]
    public static void Main(string[] args)
    {
        // Must run before ANY Microsoft.Build.*/Microsoft.CodeAnalysis.MSBuild.*
        // type is touched anywhere in the app — tells Roslyn's MSBuildWorkspace
        // which actual MSBuild installation (from the .NET SDK) to use.
        // Literally the first line of Main() on purpose.
        MSBuildLocator.RegisterDefaults();

        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
