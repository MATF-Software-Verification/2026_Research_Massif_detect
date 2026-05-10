using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace MassifVisualizer;

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
            var win = new MainWindow();
            desktop.MainWindow = win;

            // Load file passed as command-line argument
            var args = desktop.Args;
            if (args is { Length: > 0 } && System.IO.File.Exists(args[0]))
                win.LoadFileFromArgs(args[0]);
        }

        base.OnFrameworkInitializationCompleted();
    }
}