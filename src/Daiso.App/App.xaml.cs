using Daiso.App.Services;
using Daiso.App.ViewModels;
using Daiso.Core;
using Daiso.Infrastructure;
using Daiso.Providers.Claude;
using Daiso.Providers.Codex;
using Daiso.Providers.Antigravity;
using Daiso.Providers.Common;
using Daiso.Providers.Manifest;
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

        // 도구의 표시 규칙(이름·색·로고)은 도구가 들고 있다. 화면이 묻는 창구에 한 번 심어 둔다
        // (docs/PLUGIN_PLAN.md Stage 2). 플러그인을 다시 읽으면 여기를 다시 부른다
        Services.GetRequiredService<ToolRegistry>().Refresh();

        _crashReporter = new CrashReporter(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
        _crashReporter.Hook(this);
    }

    [System.Runtime.InteropServices.DllImport("ole32.dll")]
    private static extern int OleInitialize(IntPtr reserved);

    /// <summary>화면에서 ViewModel을 꺼내 쓰는 통로.</summary>
    public static IServiceProvider Services { get; private set; } = null!;

    /// <summary>
    /// 창이 닫힐 때 컨테이너를 해제한다. 싱글턴 중 <see cref="IDisposable"/> 인 것들이 정리된다
    /// (<c>SqliteSessionIndex</c> 의 쓰기 연결 등). 해제 자체가 실패해도 종료를 막지 않는다.
    /// </summary>
    public static void DisposeServices()
    {
        if (Services is IDisposable disposable)
        {
            try
            {
                disposable.Dispose();
            }
            catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
            {
                // 이미 닫혔거나 닫는 중이다. 종료 경로에서 더 할 일이 없다.
            }
        }
    }

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

        // 플러그인 어댑터와 말이 통하는지 배경에서 한 번 물어본다 (docs/PLUGIN_PLAN.md Stage 5).
        // 창이 뜬 다음에 하는 이유: 남이 만든 프로세스를 띄우는 일이라 앱 시작을 붙잡으면 안 된다
        _ = Services.GetRequiredService<ToolPluginCatalog>().VerifyAsync(CancellationToken.None);
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

        // 플러그인 도구. %USERPROFILE%\.daiso	ools\*.yaml 를 읽는다 (docs/PLUGIN_PLAN.md Stage 4).
        //
        // 컨테이너를 만들기 전에 읽는다. `IEnumerable<IProvider>` 를 직접 등록해 합치려 하면
        // 그 팩터리 안의 GetServices<IProvider>() 가 자기 자신을 다시 불러 끝없이 돈다.
        // 여기서 읽어 하나씩 IProvider 로 등록하면 컨테이너의 기본 동작(등록된 것 전부)이 그대로 산다
        var plugins = LoadPlugins();
        services.AddSingleton(plugins);

        foreach (var tool in plugins.Tools)
        {
            services.AddSingleton<IProvider>(tool);
        }

        // 도구 목록을 세는 곳. 표시 규칙을 화면 창구에 심는 일도 여기서 한다 (docs/PLUGIN_PLAN.md Stage 2)
        services.AddSingleton<ToolRegistry>();

        // Infrastructure
        services.AddSingleton<IRuleFileService, RuleFileService>();
        services.AddSingleton<IAuthProfileStore, AuthProfileStore>();
        services.AddSingleton<IPromptLibrary, PromptLibraryStore>();
        services.AddSingleton<IInstructionMigrationService>(provider =>
            new InstructionMigrationService(provider.GetServices<IProvider>()));
        services.AddSingleton<IFileDisposer, RecycleBinFileDisposer>();
        services.AddSingleton<ITerminalLauncher, WindowsTerminalLauncher>();
        services.AddSingleton<IUriOpener, ShellUriOpener>();
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

        // 대화상자와 화면 이동. 셸이 창을 띄운 뒤 XamlRoot·이동 함수를 붙여 준다 (docs/REVIEW_BACKLOG.md D1)
        services.AddSingleton<DialogHost>();
        services.AddSingleton<IDialogHost>(provider => provider.GetRequiredService<DialogHost>());
        services.AddSingleton<Navigator>();
        services.AddSingleton<INavigator>(provider => provider.GetRequiredService<Navigator>());
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

    /// <summary>
    /// 플러그인 폴더를 읽는다. 컨테이너가 서기 전이라 설정은 직접 만들어 본다 —
    /// <see cref="SettingsStore"/> 는 의존성이 없다.
    /// <para>
    /// <b>여기서 던지지 않는다.</b> 플러그인이 앱을 못 뜨게 하면 고칠 길이 없다.
    /// 무엇이 틀렸는지는 <see cref="ToolPluginCatalog"/> 가 들고 있다가 설정 화면이 보여 준다(Stage 6).
    /// </para>
    /// </summary>
    private static ToolPluginCatalog LoadPlugins()
    {
        try
        {
            var overridden = new SettingsStore().Current.SessionHomeOverride;
            var home = string.IsNullOrWhiteSpace(overridden)
                ? ProviderHome.FromUserProfile()
                : new ProviderHome(overridden);

            return new ToolPluginCatalog(new ToolPluginLoader(ToolPluginLoader.DefaultDirectory, home));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ToolPluginCatalog.Empty(ex.Message);
        }
    }
}
