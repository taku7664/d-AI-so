using System.Text;
using Daiso.Core;

namespace Daiso.Infrastructure;

/// <summary>
/// .daiso 파일 읽기·쓰기와 도구별 지시문 연동. (ARCHITECTURE §3.3, §5.2)
/// 텍스트 IO는 항상 UTF-8, BOM 없음.
/// </summary>
public sealed class RuleFileService : IRuleFileService
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly IRulePresetSerializer _serializer;
    private readonly IInstructionMarkerWriter _markerWriter;
    private readonly IInstructionTemplate _template;

    public RuleFileService()
        : this(new RulePresetSerializer(), new InstructionMarkerWriter(), new InstructionTemplate())
    {
    }

    public RuleFileService(
        IRulePresetSerializer serializer,
        IInstructionMarkerWriter markerWriter,
        IInstructionTemplate template)
    {
        ArgumentNullException.ThrowIfNull(serializer);
        ArgumentNullException.ThrowIfNull(markerWriter);
        ArgumentNullException.ThrowIfNull(template);

        _serializer = serializer;
        _markerWriter = markerWriter;
        _template = template;
    }

    /// <summary>프로젝트에 놓이는 규칙 파일 이름.</summary>
    public string RulesFileName => InstructionTemplate.DefaultRulesFileName;

    /// <inheritdoc />
    public RulePreset Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return _serializer.Parse(File.ReadAllText(path, Encoding.UTF8));
    }

    /// <inheritdoc />
    public void Save(RulePreset preset, string path)
    {
        ArgumentNullException.ThrowIfNull(preset);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, _serializer.Serialize(preset), Utf8NoBom);
    }

    /// <inheritdoc />
    /// <remarks>
    /// 도구별 지시문 파일의 daiso 마커 블록만 만들거나 갱신한다. 블록 밖은 건드리지 않는다.
    /// 같은 인자로 여러 번 실행해도 결과가 같다.
    /// </remarks>
    public void EnsureInstruction(string projectDir, IEnumerable<IProvider> providers)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDir);
        ArgumentNullException.ThrowIfNull(providers);

        Directory.CreateDirectory(projectDir);

        foreach (var provider in providers)
        {
            var path = Path.Combine(projectDir, provider.RulesFileName);
            var existing = File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : null;
            var body = _template.For(provider.Kind, RulesFileName);
            var updated = _markerWriter.Apply(existing, body);

            if (!string.Equals(existing, updated, StringComparison.Ordinal))
            {
                File.WriteAllText(path, updated, Utf8NoBom);
            }
        }
    }
}
