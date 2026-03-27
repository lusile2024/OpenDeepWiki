using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace OpenDeepWiki.Services.Wiki;

public static class WorkflowTemplateWorkbenchAiResponseParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private static readonly string[] DraftListFields =
    [
        "anchorDirectories",
        "anchorNames",
        "primaryTriggerDirectories",
        "compensationTriggerDirectories",
        "schedulerDirectories",
        "serviceDirectories",
        "repositoryDirectories",
        "primaryTriggerNames",
        "compensationTriggerNames",
        "schedulerNames",
        "requestEntityNames",
        "requestServiceNames",
        "requestRepositoryNames"
    ];

    private static readonly string[] DocumentPreferenceListFields =
    [
        "preferredTerms",
        "requiredSections",
        "avoidPrimaryTriggerNames"
    ];

    public static WorkflowTemplateWorkbenchAiResult Parse(string content)
    {
        var rawContent = StripCodeFenceAndThinkTags(content.Trim());
        var json = ExtractJsonObject(rawContent);
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidOperationException("AI 未返回可解析的模板 JSON。");
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

        throw new InvalidOperationException("AI 返回的模板结构无法解析。");
    }

    private static bool TryDeserialize(string json, out WorkflowTemplateWorkbenchAiResult result)
    {
        result = new WorkflowTemplateWorkbenchAiResult();

        try
        {
            var node = JsonNode.Parse(json);
            if (node is not JsonObject root)
            {
                return false;
            }

            NormalizeRoot(root);
            var normalizedJson = root.ToJsonString(JsonOptions);
            var parsed = JsonSerializer.Deserialize<WorkflowTemplateWorkbenchAiResult>(normalizedJson, JsonOptions);
            if (parsed?.UpdatedDraft is null)
            {
                return false;
            }

            result = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static void NormalizeRoot(JsonObject root)
    {
        NormalizeStringArray(root, "riskNotes");
        NormalizeStringArray(root, "evidenceFiles");
        PromoteAlias(root, "draft", "updatedDraft");
        PromoteAlias(root, "profile", "updatedDraft");
        PromoteAlias(root, "workflowProfile", "updatedDraft");

        if (root["updatedDraft"] is not JsonObject updatedDraft)
        {
            return;
        }

        foreach (var field in DraftListFields)
        {
            NormalizeStringArray(updatedDraft, field);
        }

        if (updatedDraft["documentPreferences"] is JsonObject documentPreferences)
        {
            foreach (var field in DocumentPreferenceListFields)
            {
                NormalizeStringArray(documentPreferences, field);
            }
        }
    }

    private static void PromoteAlias(JsonObject root, string aliasName, string targetName)
    {
        if (root[targetName] is null && root[aliasName] is not null)
        {
            root[targetName] = root[aliasName]?.DeepClone();
        }
    }

    private static void NormalizeStringArray(JsonObject root, string propertyName)
    {
        if (!root.TryGetPropertyValue(propertyName, out var node) || node is null)
        {
            root[propertyName] = new JsonArray();
            return;
        }

        switch (node)
        {
            case JsonArray array:
            {
                var normalized = new JsonArray();
                foreach (var item in array)
                {
                    var value = item?.GetValue<string>();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        normalized.Add(value.Trim());
                    }
                }

                root[propertyName] = normalized;
                break;
            }
            case JsonValue value:
            {
                var scalar = value.ToString();
                root[propertyName] = string.IsNullOrWhiteSpace(scalar)
                    ? new JsonArray()
                    : new JsonArray(scalar.Trim());
                break;
            }
            default:
                root[propertyName] = new JsonArray();
                break;
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
