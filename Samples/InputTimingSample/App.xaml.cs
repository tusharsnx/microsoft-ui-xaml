using System;
using System.IO;
using Microsoft.UI.Xaml;

namespace InputTimingSample;

public partial class App : Application
{
    private Window? m_window;

    public App()
    {
        this.InitializeComponent();

        this.UnhandledException += (sender, e) =>
        {
            LogCrash("App.UnhandledException", e.Exception, e.Message);
        };

        AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
        {
            LogCrash("AppDomain.CurrentDomain.UnhandledException", e.ExceptionObject as Exception, e.ExceptionObject?.ToString() ?? "Unknown error");
        };
    }

    private static void LogCrash(string source, Exception? ex, string message)
    {
        try
        {
            string crashInfo = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {source}\nMessage: {message}\nException: {ex?.GetType().FullName}: {ex?.Message}\nStackTrace:\n{ex?.StackTrace}\nInnerException: {ex?.InnerException}\n----------------------------------------\n";
            File.AppendAllText("D:\\dev\\microsoft-ui-xaml\\crash.log", crashInfo);
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "InputTimingSample_crash.log"), crashInfo);
        }
        catch { }
    }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        m_window = new MainWindow();
        m_window.Activate();
    }
}
