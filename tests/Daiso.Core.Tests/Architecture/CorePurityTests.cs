using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Daiso.Core;

namespace Daiso.Core.Tests.Architecture;

/// <summary>
/// ARCHITECTURE §1 "Core 순수성 규칙" 검증.
/// Daiso.Core 어셈블리의 IL 메타데이터에서 금지된 타입·멤버 참조가 없음을 확인한다.
/// </summary>
public sealed class CorePurityTests
{
    /// <summary>타입 전체가 금지된 것 (어떤 멤버도 호출 불가).</summary>
    private static readonly ImmutableHashSet<string> ForbiddenTypes = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "System.IO.File",
        "System.IO.FileInfo",
        "System.IO.Directory",
        "System.IO.DirectoryInfo",
        "System.IO.FileSystemInfo",
        "System.Diagnostics.Process",
        "System.Diagnostics.ProcessStartInfo");

    /// <summary>특정 멤버만 금지된 것. "타입.멤버" 형식.</summary>
    private static readonly ImmutableHashSet<string> ForbiddenMembers = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "System.IO.Path.GetFullPath",
        "System.Environment.GetFolderPath",
        "System.Environment.GetEnvironmentVariable",
        "System.Environment.GetEnvironmentVariables",
        "System.Environment.ExpandEnvironmentVariables");

    [Fact]
    public void Core_does_not_reference_file_path_or_process_apis()
    {
        var violations = FindForbiddenReferences(typeof(ToolKind).Assembly);

        violations.Should().BeEmpty(
            "Core는 경로·파일·프로세스·환경변수를 다루지 않는다 (ARCHITECTURE §1, §7.5). 발견된 참조: {0}",
            string.Join(", ", violations));
    }

    private static IReadOnlyList<string> FindForbiddenReferences(Assembly assembly)
    {
        var path = assembly.Location;
        path.Should().NotBeNullOrEmpty("어셈블리 파일 경로를 찾을 수 없다");

        using var stream = System.IO.File.OpenRead(path);
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();

        var violations = new List<string>();

        foreach (var handle in reader.MemberReferences)
        {
            var memberRef = reader.GetMemberReference(handle);
            if (memberRef.Parent.Kind != HandleKind.TypeReference)
            {
                continue;
            }

            var typeName = GetFullTypeName(reader, (TypeReferenceHandle)memberRef.Parent);
            var memberName = reader.GetString(memberRef.Name);

            if (ForbiddenTypes.Contains(typeName))
            {
                violations.Add($"{typeName}.{memberName}");
            }
            else if (ForbiddenMembers.Contains($"{typeName}.{memberName}"))
            {
                violations.Add($"{typeName}.{memberName}");
            }
        }

        return violations.Distinct(StringComparer.Ordinal).OrderBy(v => v, StringComparer.Ordinal).ToList();
    }

    private static string GetFullTypeName(MetadataReader reader, TypeReferenceHandle handle)
    {
        var typeRef = reader.GetTypeReference(handle);
        var name = reader.GetString(typeRef.Name);

        if (typeRef.ResolutionScope.Kind == HandleKind.TypeReference)
        {
            var outer = GetFullTypeName(reader, (TypeReferenceHandle)typeRef.ResolutionScope);
            return $"{outer}+{name}";
        }

        var ns = typeRef.Namespace.IsNil ? null : reader.GetString(typeRef.Namespace);
        return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
    }
}
