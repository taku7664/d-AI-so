using Daiso.App.Services;
using Daiso.App.ViewModels;
using Daiso.Core;
using Daiso.Infrastructure;
using Daiso.Providers.Claude;
using Daiso.Providers.Codex;
using Daiso.Providers.Antigravity;
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
        // 파일 끌어놓기(OLE 드래그)를 받으려면 UI 스레드가 OleInitialize 돼 있어야 XAML 이 창을 드롭 대상으로 등록한다.
        // 생성된 Main 은 COM(STA)만 초기화하므로, 이 앱에서는 이게 없으면 창 어디에도 드롭 대상이 등록되지 않아 금지 커서가 뜬다
        // (2026-09-09 측정: 모든 HWND 에 OleDropTargetInterface 속성 없음). 이미 초기화돼 있으면 S_FALSE 로 그냥 넘어간다.
        _ = OleInitialize(IntPtr.Zero);

        InitializeComponent();
        Services = BuildServices();

        _crashReporter = new CrashReporter(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
        _crashReporter.Hook(this);
    }

    [System.Runtime.InteropServices.DllImport("ole32.dll")]
    private static extern int OleInitialize(IntPtr reserved);

    /// <summary>화면에서 ViewModel을 꺼내 쓰는 통로.</summary>
    public static IServiceProvider Services { get; private set; } = null!;

    /// <summary>열려 있는 Shell 창. 파일 선택 대화상자가 창 핸들을 필요로 한다.</summary>
    public static Window? MainWindow { get; private set; }

    /// <summary>열린 방을 들고 있는 하나뿐인 자리. 화면을 옮겨도 방은 여기 살아 있다. (ARCHITECTURE §5.3)</summary>
    public static ViewModels.RoomManager Rooms => Services.GetRequiredService<ViewModels.RoomManager>();

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
            new AntigravityProvider(provider.GetRequiredService<ProviderHome>()));

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
        services.AddSingleton(provider => new SlashCommandReader(provider.GetRequiredService<ProviderHome>()));
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

        services.AddSingleton<ViewModels.RoomManager>();
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
