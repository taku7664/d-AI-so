using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Daiso.Infrastructure;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Daiso.App.Strings;

namespace Daiso.App.Services;

/// <summary>
/// 처리되지 않은 예외를 사용자에게 알리고 로그 파일에 남긴다.
/// 로그에 남기기 전에 토큰처럼 보이는 문자열을 가린다. (ARCHITECTURE §7.1)
/// </summary>
public sealed class CrashReporter
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly DispatcherQueue _dispatcher;
    private Window? _window;

    public CrashReporter(DispatcherQueue dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        _dispatcher = dispatcher;
    }

    /// <summary>로그 파일이 쌓이는 폴더.</summary>
    public static string LogDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "d-AI-so",
        "logs");

    /// <summary>대화상자를 띄울 창. 창이 생긴 뒤에 붙인다.</summary>
    public void Attach(Window window) => _window = window;

    /// <summary>앱 전역 예외 통로를 모두 잇는다.</summary>
    public void Hook(Application application)
    {
        ArgumentNullException.ThrowIfNull(application);

        application.UnhandledException += (_, args) =>
        {
            args.Handled = true;
            Report("UI", args.Exception);
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Report("AppDomain", args.ExceptionObject as Exception);

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            args.SetObserved();
            Report("Task", args.Exception);
        };
    }

    /// <summary>예외 하나를 기록하고 알린다.</summary>
    public void Report(string source, Exception? exception)
    {
        var path = Write(source, exception);

        _dispatcher.TryEnqueue(async () =>
        {
            if (_window?.Content?.XamlRoot is not { } xamlRoot)
            {
                return;
            }

            var body =
                $"{SensitiveTextMasker.MaskSensitive(exception?.Message) ?? UiStrings.Get("Crash_UnknownError")}\n\n"
                + (path is null
                    ? UiStrings.Get("Crash_LogWriteFailed")
                    : UiStrings.Format("Crash_LogPath", path));

            try
            {
                await new ContentDialog
                {
                    XamlRoot = xamlRoot,
                    Title = UiStrings.Get("Crash_Title"),
                    Content = new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap },
                    CloseButtonText = UiStrings.Get("Common_Close"),
                }.ShowAsync();
            }
            catch (Exception ex) when (ex is InvalidOperationException or COMException)
            {
                // 이미 다른 대화상자가 떠 있으면 조용히 넘어간다. 로그는 이미 남았다.
            }
        });
    }

    /// <summary>스택까지 로그 파일에 남긴다. 토큰은 가린다.</summary>
    private static string? Write(string source, Exception? exception)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);

            var name = $"crash-{DateTime.Now:yyyyMMdd-HHmmss}.log";
            var path = Path.Combine(LogDirectory, name);

            var text = new StringBuilder()
                .AppendLine(CultureInfo.InvariantCulture, $"시각: {DateTimeOffset.Now:O}")
                .AppendLine(CultureInfo.InvariantCulture, $"출처: {source}")
                .AppendLine()
                .AppendLine(exception?.ToString() ?? "(예외 객체 없음)")
                .ToString();

            File.WriteAllText(path, SensitiveTextMasker.MaskSensitive(text), Utf8NoBom);

            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
