using Daiso.Core;
using Daiso.Core.Prompts;
using Daiso.Providers.Common;

namespace Daiso.Infrastructure.Tests;

/// <summary>FEATURE_PLAN `/` 선택기 — 내장 표에 디스크의 사용자·프로젝트·스킬 명령을 합친다.</summary>
public sealed class SlashCommandReaderTests
{
    [Fact]
    public void Built_in_commands_come_first_even_with_an_empty_home()
    {
        var home = Directory.CreateTempSubdirectory("daiso-slash-empty-");
        try
        {
            var reader = new SlashCommandReader(new ProviderHome(home.FullName));
            var commands = reader.Read(ToolKind.Claude, null);

            commands.Should().Contain(c => c.Name == "compact" && c.Source == SlashCommandSource.BuiltIn);
            commands.Should().Contain(c => c.Invocation == "/model");
        }
        finally
        {
            home.Delete(recursive: true);
        }
    }

    [Fact]
    public void Claude_reads_user_commands_project_commands_and_skills()
    {
        var home = Directory.CreateTempSubdirectory("daiso-slash-claude-");
        var project = Directory.CreateTempSubdirectory("daiso-slash-proj-");
        try
        {
            var userCmd = Path.Combine(home.FullName, ".claude", "commands");
            Directory.CreateDirectory(userCmd);
            File.WriteAllText(Path.Combine(userCmd, "deploy.md"), "---\ndescription: 배포 절차\n---\n# Deploy\n본문");

            var projCmd = Path.Combine(project.FullName, ".claude", "commands");
            Directory.CreateDirectory(projCmd);
            File.WriteAllText(Path.Combine(projCmd, "test.md"), "# 테스트 실행\n프로젝트 전용");

            var skillDir = Path.Combine(home.FullName, ".claude", "skills", "codec");
            Directory.CreateDirectory(skillDir);
            File.WriteAllText(Path.Combine(skillDir, "SKILL.md"), "---\nname: codec\ndescription: 파일 코덱\n---\n# Codec");

            var reader = new SlashCommandReader(new ProviderHome(home.FullName));
            var commands = reader.Read(ToolKind.Claude, project.FullName);

            commands.Should().Contain(c => c.Name == "deploy" && c.Description == "배포 절차" && c.Source == SlashCommandSource.User);
            commands.Should().Contain(c => c.Name == "test" && c.Description == "테스트 실행" && c.Source == SlashCommandSource.Project);
            commands.Should().Contain(c => c.Name == "codec" && c.Description == "파일 코덱" && c.Source == SlashCommandSource.Skill);
        }
        finally
        {
            home.Delete(recursive: true);
            project.Delete(recursive: true);
        }
    }

    [Fact]
    public void Gemini_reads_toml_description_and_folder_namespaces_the_name()
    {
        var home = Directory.CreateTempSubdirectory("daiso-slash-gem-");
        try
        {
            var cmd = Path.Combine(home.FullName, ".gemini", "commands", "git");
            Directory.CreateDirectory(cmd);
            File.WriteAllText(Path.Combine(cmd, "commit.toml"), "description = \"커밋 메시지 작성\"\nprompt = \"...\"");

            var reader = new SlashCommandReader(new ProviderHome(home.FullName));
            var commands = reader.Read(ToolKind.Gemini, null);

            commands.Should().Contain(c => c.Name == "git:commit" && c.Description == "커밋 메시지 작성" && c.Source == SlashCommandSource.User);
            commands.Should().Contain(c => c.Name == "compress" && c.Source == SlashCommandSource.BuiltIn);
        }
        finally
        {
            home.Delete(recursive: true);
        }
    }

    [Fact]
    public void A_duplicate_name_keeps_the_earlier_source()
    {
        var home = Directory.CreateTempSubdirectory("daiso-slash-dup-");
        try
        {
            var userCmd = Path.Combine(home.FullName, ".claude", "commands");
            Directory.CreateDirectory(userCmd);
            // 내장에 이미 있는 compact를 사용자도 정의 → 내장(먼저 넣은 것)이 남는다
            File.WriteAllText(Path.Combine(userCmd, "compact.md"), "---\ndescription: 내 버전\n---\n");

            var reader = new SlashCommandReader(new ProviderHome(home.FullName));
            var compact = reader.Read(ToolKind.Claude, null).Where(c => c.Name == "compact").ToList();

            compact.Should().ContainSingle();
            compact[0].Source.Should().Be(SlashCommandSource.BuiltIn);
        }
        finally
        {
            home.Delete(recursive: true);
        }
    }
}
