using System.Text;
using System.Text.RegularExpressions;

namespace OpenDeepWiki.Services.Wiki;

public static partial class WorkflowRequiredSectionEnforcer
{
    public static WorkflowRequiredSectionEnforcementResult Enforce(
        string content,
        WorkflowDocumentPreferences? preferences)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return new WorkflowRequiredSectionEnforcementResult(content, []);
        }

        var requiredSections = preferences?.RequiredSections
            .Where(section => !string.IsNullOrWhiteSpace(section))
            .Select(section => section.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];

        if (requiredSections.Count == 0)
        {
            return new WorkflowRequiredSectionEnforcementResult(content, []);
        }

        var missingSections = requiredSections
            .Where(section => !ContainsSectionHeading(content, section))
            .ToList();

        if (missingSections.Count == 0)
        {
            return new WorkflowRequiredSectionEnforcementResult(content, []);
        }

        var builder = new StringBuilder(content.TrimEnd());
        builder.AppendLine();
        builder.AppendLine();

        foreach (var section in missingSections)
        {
            builder.AppendLine($"## {section}");
            builder.AppendLine();
            builder.AppendLine("> 本节是 workflow profile 配置中的必备章节。当前自动生成结果未覆盖该章节，请结合证据文件补充具体实现；若证据不足，请明确标注待确认点。");
            builder.AppendLine();
        }

        return new WorkflowRequiredSectionEnforcementResult(builder.ToString().TrimEnd(), missingSections);
    }

    private static bool ContainsSectionHeading(string content, string section)
    {
        if (string.IsNullOrWhiteSpace(section))
        {
            return true;
        }

        var escapedSection = Regex.Escape(section.Trim());
        var pattern =
            $@"^\s{{0,3}}#{{1,6}}\s+(?:\d+(?:\.\d+)*[\.、\)]\s*)?{escapedSection}(?:\s*[:：-].*|\s*[（(].*[)）])?\s*$";

        return Regex.IsMatch(content, pattern, RegexOptions.Multiline | RegexOptions.IgnoreCase);
    }
}

public sealed record WorkflowRequiredSectionEnforcementResult(
    string Content,
    IReadOnlyList<string> MissingSections);
