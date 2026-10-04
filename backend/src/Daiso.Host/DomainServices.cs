using Daiso.Core;
using Daiso.Host.Services;
using Daiso.Infrastructure;
using Daiso.Providers.Antigravity;
using Daiso.Providers.Claude;
using Daiso.Providers.Codex;
using Daiso.Providers.Common;
using Daiso.Providers.Manifest;

namespace Daiso.Host;

/// <summary>
/// old/docs/ARCHITECTURE.md §3 의 인터페이스를 구현으로 잇는다. 옛 앱 <c>App.xaml.cs</c> 의 <c>BuildServices</c> 에서
/// ViewModel·창·대화상자를 빼고 옮겼다 (docs/MIGRATION_MAP.md).
/// <para>
/// 전부 싱글턴이고 <b>처음 물을 때 만든다.</b> 인덱스 DB 는 그 서비스를 쓰는 탭이 생기기 전까지 열리지 않는다.
/// </para>
/// </summary>
public static class DomainServices
{
    public static IServiceCollection AddDaisoDomain(this IServiceCollection services, DaisoHostOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        var settingsPath = Path.Combine(options.DataDirectory, SettingsStore.FileName);
        services.AddSingleton<ISettingsStore>(_ => new SettingsStore(settingsPath));

        // Core (순수)
        services.AddSingleton<IRulePresetSerializer, RulePresetSerializer>();
        services.AddSingleton<IMarkdownRuleRenderer, MarkdownRuleRenderer>();
        services.AddSingleton<IInstructionMarkerWriter, InstructionMarkerWriter>();
        services.AddSingleton<IInstructionTemplate, InstructionTemplate>();
        services.AddSingleton<IContextAnalyzer, ContextAnalyzer>();

        // Providers — 세션 루트는 설정으로 바꿀 수 있다 (삭제 검증용 더미 폴더 등)
        services.AddSingleton(provider => Home(provider.GetRequiredService<ISettingsStore>().Current));
        services.AddSingleton<IProvider>(provider =>
            new ClaudeProvider(provider.GetRequiredService<ProviderHome>(), new ProcessProbe()));
        services.AddSingleton<IProvider>(provider =>
            new CodexProvider(provider.GetRequiredService<ProviderHome>()));
        services.AddSingleton<IProvider>(provider =>
            new AntigravityProvider(provider.GetRequiredService<ProviderHome>()));

        // 플러그인 도구. 컨테이너를 만들기 전에 읽는다. `IEnumerable<IProvider>` 를 팩터리로 합치려 하면
        // 그 안의 GetServices<IProvider>() 가 자기 자신을 다시 불러 끝없이 돈다 (옛 App.xaml.cs 와 같은 이유)
        var plugins = LoadPlugins(options, settingsPath);
        services.AddSingleton(plugins);

        foreach (var tool in plugins.Tools)
        {
            services.AddSingleton<IProvider>(tool);
        }

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
        // 설정에 적힌 인덱스 경로를 쓸 수 없으면 기본 경로로 물러선다
        services.AddSingleton(provider =>
            IndexLocation.Resolve(provider.GetRequiredService<ISettingsStore>().Current.IndexDatabasePath));
        services.AddSingleton<ISessionIndex>(provider =>
            new SqliteSessionIndex(
                provider.GetRequiredService<IEnumerable<IProvider>>(),
                provider.GetRequiredService<IndexLocation>().Path));

        return services;
    }

    private static ProviderHome Home(AppSettings settings) =>
        string.IsNullOrWhiteSpace(settings.SessionHomeOverride)
            ? ProviderHome.FromUserProfile()
            : new ProviderHome(settings.SessionHomeOverride);

    /// <summary>
    /// 플러그인 폴더를 읽는다. <b>여기서 던지지 않는다.</b> 플러그인이 서버를 못 뜨게 하면 고칠 길이 없다.
    /// 무엇이 틀렸는지는 <see cref="ToolPluginCatalog"/> 가 들고 있다.
    /// </summary>
    private static ToolPluginCatalog LoadPlugins(DaisoHostOptions options, string settingsPath)
    {
        try
        {
            var home = Home(new SettingsStore(settingsPath).Current);

            return new ToolPluginCatalog(options.PluginDirectory, new ToolPluginLoader(options.PluginDirectory, home));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ToolPluginCatalog.Empty(options.PluginDirectory, ex.Message);
        }
    }
}
