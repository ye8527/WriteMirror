using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WriteMirror.App;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// WinUI 3 requires the application to retain its Window instance.
    /// </summary>
    private Window? _window;

    /// <summary>
    /// Initializes the singleton application object.
    /// </summary>
    public App()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Creates and activates the main window after the application launches.
    /// </summary>
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new Window
        {
            Title = "WriteMirror"
        };

        try
        {
            _window.Content = new MainPage();
        }
        catch (Exception error)
        {
            WriteStartupError(error);
            _window.Content = CreateStartupErrorView(error);
        }

        _window.Activate();
    }

    private static FrameworkElement CreateStartupErrorView(Exception error)
    {
        var panel = new StackPanel
        {
            Padding = new Thickness(32),
            Spacing = 12
        };
        panel.Children.Add(new TextBlock
        {
            Text = "WriteMirror を起動しました",
            FontSize = 28,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        panel.Children.Add(new TextBlock
        {
            Text = "メイン画面の読み込み中に問題が発生しました。診断ログを保存しました。",
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(new TextBlock
        {
            Text = $"{error.GetType().Name}: {error.Message}",
            TextWrapping = TextWrapping.Wrap
        });
        return panel;
    }

    private static void WriteStartupError(Exception error)
    {
        try
        {
            string directory = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WriteMirror");
            Directory.CreateDirectory(directory);
            File.WriteAllText(
                System.IO.Path.Combine(directory, "startup-error.log"),
                $"{DateTimeOffset.Now:O}{Environment.NewLine}{error}");
        }
        catch
        {
            // ログ保存の失敗によって、回復画面の表示を妨げないようにします。
        }
    }
}
