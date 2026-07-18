using System.IO;
using System.Windows;

namespace WriteMirror.Wpf;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, args) =>
        {
            WriteError(args.Exception);
            MessageBox.Show(
                $"予期しない問題が発生しました。\n\n{args.Exception.Message}",
                "WriteMirror",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };

        base.OnStartup(e);
    }

    private static void WriteError(Exception error)
    {
        try
        {
            string directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WriteMirror");
            Directory.CreateDirectory(directory);
            File.AppendAllText(
                Path.Combine(directory, "wpf-error.log"),
                $"{DateTimeOffset.Now:O}{Environment.NewLine}{error}{Environment.NewLine}---{Environment.NewLine}");
        }
        catch
        {
            // エラーログの失敗によってアプリを終了させないようにします。
        }
    }
}
