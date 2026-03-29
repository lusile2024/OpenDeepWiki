using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OpenDeepWiki.Services.Wiki;

public static class WorkflowAnalysisPlannerHintAiResponseParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static WorkflowAnalysisPlannerHintAiResult Parse(string content)
    {
        var rawContent = StripCodeFenceAndThinkTags(content.Trim());
        var json = ExtractJsonObject(rawContent);
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidOperationException("AI 未返回可解析的 planner hint JSON。");
        }

        if (TryDeserialize(json, out var result))
        {
            return result;
        }

        var repairedJson = RepairLooseJson(json);
        if (TryDeserialize(repairedJson, out result))
        {
            return result;
        }

        throw new InvalidOperationException("AI 返回的 planner hint 结构无法解析。");
    }

    private static bool TryDeserialize(string json, out WorkflowAnalysisPlannerHintAiResult result)
    {
        result = new WorkflowAnalysisPlannerHintAiResult();

        try
        {
            var parsed = JsonSerializer.Deserialize<WorkflowAnalysisPlannerHintAiResult>(json, JsonOptions);
            if (parsed is null)
            {
                return false;
            }

            parsed.SuggestedBranchTasks ??= [];
            result = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string StripCodeFenceAndThinkTags(string text)
    {
        var withoutThink = Regex.Replace(text, "<think>[\\s\\S]*?</think>", string.Empty, RegexOptions.IgnoreCase);
        var trimmed = withoutThink.Trim();

        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewLine = trimmed.IndexOf('\n');
            if (firstNewLine >= 0)
            {
                trimmed = trimmed[(firstNewLine + 1)..];
            }

            if (trimmed.EndsWith("```", StringComparison.Ordinal))
            {
                trimmed = trimmed[..^3];
            }
        }

        return trimmed.Trim();
    }

    private static string? ExtractJsonObject(string content)
    {
        var firstBrace = content.IndexOf('{');
        var lastBrace = content.LastIndexOf('}');
        if (firstBrace < 0 || lastBrace <= firstBrace)
        {
            return null;
        }

        return content[firstBrace..(lastBrace + 1)];
    }

    private static string RepairLooseJson(string json)
    {
        var builder = new StringBuilder(json.Length + 32);
        var inString = false;
        var escaped = false;

        for (var index = 0; index < json.Length; index++)
        {
            var current = json[index];

            if (!inString)
            {
                if (current == '"')
                {
                    inString = true;
                }

                builder.Append(current);
                continue;
            }

            if (escaped)
            {
                builder.Append(current);
                escaped = false;
                continue;
            }

            if (current == '\\')
            {
                builder.Append(current);
                escaped = true;
                continue;
            }

            if (current == '"')
            {
                var nextSignificant = GetNextNonWhitespaceChar(json, index + 1);
                if (nextSignificant is null or ',' or '}' or ']' or ':')
                {
                    inString = false;
                    builder.Append(current);
                }
                else
                {
                    builder.Append("\\\"");
                }

                continue;
            }

            if (current == '\r')
            {
                builder.Append("\\r");
                continue;
            }

            if (current == '\n')
            {
                builder.Append("\\n");
                continue;
            }

            if (current == '\t')
            {
                builder.Append("\\t");
                continue;
            }

            builder.Append(current);
        }

        return builder.ToString();
    }

    private static char? GetNextNonWhitespaceChar(string text, int startIndex)
    {
        for (var index = startIndex; index < text.Length; index++)
        {
            if (!char.IsWhiteSpace(text[index]))
            {
                return text[index];
            }
        }

        return null;
    }
}
