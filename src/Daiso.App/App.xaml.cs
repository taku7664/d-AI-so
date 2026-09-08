using Daiso.App.Services;
using Daiso.App.ViewModels;
using Daiso.Core;
using Daiso.Infrastructure;
using Daiso.Providers.Claude;
using Daiso.Providers.Codex;
using Daiso.Providers.Gemini;
using Daiso.Providers.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;

namespace Daiso.App;

/// <summary>앱 진입점. DI 컨테이너를 만들고 Shell 창을 띄운다. (ARCHITECTURE §6)</summary>
public partial class App : Application
{
    private readonly CrashReporter _crashReporter;

    private Window? _window;

    public App()
    {
        InitializeComponent();
        Services = BuildServices();

        _crashReporter = new CrashReporter(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
        _crashReporter.Hook(this);
    }

    /// <summary>화면에서 ViewModel을 꺼내 쓰는 통로.</summary>
    public static IServiceProvider Services { get; private set; } = null!;

    /// <summary>열려 있는 Shell 창. 파일 선택 대화상자가 창 핸들을 필요로 한다.</summary>
    public static Window? MainWindow { get; private set; }

    /// <summary>
    /// 지금 살아 있는 대화 방. 앱을 닫을 때 여기 든 방을 정리해 자식 CLI 프로세스가 남지 않게 한다.
    /// 페이지를 옮겨도 방은 이 목록으로 살아 있다. (ARCHITECTURE §5.3)
    /// </summary>
    private static readonly List<ViewModels.ChatRoomViewModel> Rooms = [];

    /// <summary>방을 등록한다. 이미 끝난 방은 걷어낸다.</summary>
    public static void RegisterRoom(ViewModels.ChatRoomViewModel room)
    {
        ArgumentNullException.ThrowIfNull(room);

        lock (Rooms)
        {
            Rooms.RemoveAll(existing => existing.HasExited);
            Rooms.Add(room);
        }
    }

    /// <summary>살아 있는 방이 있는가. 앱을 닫을 때 물어볼지 판단한다.</summary>
    public static bool HasRunningRooms()
    {
        lock (Rooms)
        {
            Rooms.RemoveAll(existing => existing.HasExited);
            return Rooms.Count > 0;
        }
    }

    /// <summary>모든 방을 정리한다(자식 프로세스 트리 kill). 앱 종료 때.</summary>
    public static void DisposeRooms()
    {
        lock (Rooms)
        {
            foreach (var room in Rooms)
            {
                room.Dispose();
            }

            Rooms.Clear();
        }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = Services.GetRequiredService<ShellWindow>();
        MainWindow = _window;
        _crashReporter.Attach(_window);
        _window.Activate();
    }

    /// <summary>
    /// ARCHITECTURE §3의 인터페이스를 구현으로 잇는다.
    /// Provider는 <c>IEnumerable&lt;IProvider&gt;</c>로 주입된다.
    /// </summary>
    private static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();

        // Core (순수)
        services.AddSingleton<IRulePresetSerializer, RulePresetSerializer>();
        services.AddSingleton<IMarkdownRuleRenderer, MarkdownRuleRenderer>();
        services.AddSingleton<IInstructionMarkerWriter, InstructionMarkerWriter>();
        services.AddSingleton<IInstructionTemplate, InstructionTemplate>();
        services.AddSingleton<IContextAnalyzer, ContextAnalyzer>();

        // Providers — 세션 루트는 설정으로 바꿀 수 있다 (삭제 검증용 더미 폴더 등)
        services.AddSingleton(provider =>
        {
            var overridden = provider.GetRequiredService<ISettingsStore>().Current.SessionHomeOverride;

            return string.IsNullOrWhiteSpace(overridden)
                ? ProviderHome.FromUserProfile()
                : new ProviderHome(overridden);
        });
        services.AddSingleton<IProvider>(provider =>
            new ClaudeProvider(provider.GetRequiredService<ProviderHome>(), new ProcessProbe()));
        services.AddSingleton<IProvider>(provider =>
            new CodexProvider(provider.GetRequiredService<ProviderHome>()));
        services.AddSingleton<IProvider>(provider =>
            new GeminiProvider(provider.GetRequiredService<ProviderHome>()));

        // Infrastructure
        services.AddSingleton<IRuleFileService, RuleFileService>();
        services.AddSingleton<IAuthProfileStore, AuthProfileStore>();
        services.AddSingleton<IPromptLibrary, PromptLibraryStore>();
        services.AddSingleton<IInstructionMigrationService>(provider =>
            new InstructionMigrationService(provider.GetServices<IProvider>()));
        services.AddSingleton<IFileDisposer, RecycleBinFileDisposer>();
        services.AddSingleton<ITerminalLauncher, WindowsTerminalLauncher>();
        services.AddSingleton<IContextInspector>(provider =>
            new ContextInspector(
                provider.GetRequiredService<IEnumerable<IProvider>>(),
                provider.GetRequiredService<IContextAnalyzer>()));
        services.AddSingleton<IProjectFactsReader, ProjectFactsReader>();
        services.AddSingleton<ISessionExporter>(provider =>
            new MarkdownSessionExporter(provider.GetRequiredService<IEnumerable<IProvider>>()));
        services.AddSingleton<ISessionIndex>(provider =>
            new SqliteSessionIndex(
                provider.GetRequiredService<IEnumerable<IProvider>>(),
                provider.GetRequiredService<ISettingsStore>().Current.IndexDatabasePath
                    ?? SqliteSessionIndex.DefaultDatabasePath));

        // App
        services.AddSingleton<ISettingsStore, SettingsStore>();
        services.AddSingleton<IndexService>();
        services.AddSingleton<KnownProjects>();

        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<UsageViewModel>();
        services.AddSingleton<TerminalViewModel>();
        services.AddSingleton<SessionsViewModel>();
        services.AddSingleton<ContextDoctorViewModel>();
        services.AddSingleton<RuleMakerViewModel>();
        services.AddSingleton<PromptsViewModel>();
        services.AddSingleton<MigrationViewModel>();
        services.AddSingleton<AuthProfileViewModel>();
        services.AddSingleton<SettingsViewModel>();

        services.AddSingleton<ShellWindow>();

        return services.BuildServiceProvider();
    }
}
