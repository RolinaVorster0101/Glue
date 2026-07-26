using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
#if DEBUG
using Avalonia.Diagnostics;
#endif

namespace Glue;

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
            desktop.MainWindow = new MainWindow();

#if DEBUG
            // F12 opens DevTools. An attempt to move this to Ctrl+F12 via
            // DevToolsOptions.Gesture didn't actually replace the default —
            // both F12 and Ctrl+F12 ended up triggering it, suggesting the
            // gesture is additive rather than a full override. Reverted to
            // the plain default rather than keep guessing at internal
            // behavior for a DEBUG-only convenience tool. Go to Definition
            // uses Ctrl+Alt+G instead, to avoid the conflict entirely.
            desktop.MainWindow.AttachDevTools();
#endif
        }

        base.OnFrameworkInitializationCompleted();
    }
}
