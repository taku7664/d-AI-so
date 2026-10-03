namespace Daiso.Core;

/// <summary>YAML 문자열 ↔ <see cref="RulePreset"/> 변환. 파일 접근은 하지 않는다. (ARCHITECTURE §3.1)</summary>
public interface IRulePresetSerializer
{
    /// <summary>YAML을 파싱하고 검증한다.</summary>
    /// <exception cref="RuleParseException">문법 또는 스키마 위반.</exception>
    RulePreset Parse(string yaml);

    /// <summary>정규 형식 YAML로 직렬화한다. 키 순서 고정, 주석·공백은 보존하지 않는다.</summary>
    string Serialize(RulePreset preset);
}
