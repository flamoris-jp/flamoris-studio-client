using System.IO;
using System.Windows;
using Flamoris.StudioClient.Infrastructure;

namespace Flamoris.StudioClient.App;

public partial class App : Application
{
    internal static string StateRoot { get; } = Environment.GetCommandLineArgs().Contains("--smoke")
        ? Path.Combine(Path.GetTempPath(), "studioclient-smoke-" + Guid.NewGuid().ToString("N"))
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FLAMORIS", "StudioClient");
    internal static StudioDiagnostics Diagnostics { get; } = new(StateRoot);
    public App()
    {
        DispatcherUnhandledException += (_, e) =>
        {
            Diagnostics.Write(e.Exception, "dispatcher.unhandled");
            // Do not globally swallow an unknown UI failure and continue corrupted state.
            if (!Environment.GetCommandLineArgs().Contains("--smoke"))
                MessageBox.Show("予期しないエラーで終了します。再起動時に復旧用データが残っていれば、編集を復元できます。\n診断ログ：%LOCALAPPDATA%/FLAMORIS/StudioClient/logs",
                    "StudioClient", MessageBoxButton.OK, MessageBoxImage.Error);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception error) Diagnostics.Write(error, "process.unhandled");
        };
    }
}
