using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace NevaVision;

public partial class App : Application
{
    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NevaVision");

    private static readonly string LogPath = Path.Combine(LogDirectory, "NevaVision-error.log");

    public App()
    {
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += App_UnhandledException;
    }

    private void App_OnStartup(object sender, StartupEventArgs e)
    {
        MainWindow = new MainWindow();
        MainWindow.Show();
    }

    private static void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        string details = WriteError(e.Exception);
        MessageBox.Show(
            $"NevaVision не смог запуститься.\n\n{e.Exception.Message}\n\nОтчёт сохранён здесь:\n{details}",
            "NevaVision",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private static void App_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
            WriteError(exception);
    }

    private static string WriteError(Exception exception)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            File.AppendAllText(
                LogPath,
                $"[{DateTimeOffset.Now:O}] {exception}\n\n");
            return LogPath;
        }
        catch
        {
            return "не удалось сохранить отчёт";
        }
    }
}
