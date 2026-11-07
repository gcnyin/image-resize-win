using Microsoft.UI.Xaml;

namespace ImageResize;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
        this.UnhandledException += (_, e) =>
        {
            var msg = $"[{DateTime.Now:HH:mm:ss}] Unhandled: {e.Message}\n{e.Exception}\n\n";
            System.IO.File.AppendAllText(@"D:\dev\image-resize-win\crash.log", msg);
            e.Handled = true;
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }
}
