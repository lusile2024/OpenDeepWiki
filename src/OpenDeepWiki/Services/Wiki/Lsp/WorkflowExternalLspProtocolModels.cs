using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenDeepWiki.Services.Wiki.Lsp;

public sealed class WorkflowExternalLspSymbolRequest
{
    public string WorkspacePath { get; set; } = string.Empty;

    public string FilePath { get; set; } = string.Empty;

    public string? SymbolName { get; set; }

    public int LineNumber { get; set; }

    public int ColumnNumber { get; set; }

    public WorkflowLspAssistOptions AssistOptions { get; set; } = new();

    public string? PreferredServer { get; set; }
}

public sealed class WorkflowExternalLspSymbolResult
{
    public bool Attempted { get; set; }

    public bool Success { get; set; }

    public string Strategy { get; set; } = "disabled";

    public string? ServerName { get; set; }

    public string? FailureReason { get; set; }

    public List<WorkflowLspDiagnostic> Diagnostics { get; set; } = [];

    public List<WorkflowLspResolvedLocation> Definitions { get; set; } = [];

    public List<WorkflowLspResolvedLocation> References { get; set; } = [];

    public List<WorkflowCallHierarchyEdge> CallHierarchyEdges { get; set; } = [];

    public List<string> SuggestedRootSymbolNames { get; set; } = [];

    public List<string> SuggestedMustExplainSymbols { get; set; } = [];
}

internal sealed class JsonRpcRequestEnvelope
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";

    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Id { get; set; }

    [JsonPropertyName("method")]
    public string Method { get; set; } = string.Empty;

    [JsonPropertyName("params")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Params { get; set; }
}

internal sealed class JsonRpcResponseEnvelope
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";

    [JsonPropertyName("id")]
    public object? Id { get; set; }

    [JsonPropertyName("result")]
    public object? Result { get; set; }
}

internal sealed class LspInitializeParams
{
    [JsonPropertyName("processId")]
    public int ProcessId { get; set; }

    [JsonPropertyName("rootUri")]
    public string RootUri { get; set; } = string.Empty;

    [JsonPropertyName("clientInfo")]
    public LspClientInfo ClientInfo { get; set; } = new();

    [JsonPropertyName("capabilities")]
    public object Capabilities { get; set; } = new();
}

internal sealed class LspClientInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "OpenDeepWiki";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0";
}

internal sealed class LspTextDocumentIdentifier
{
    [JsonPropertyName("uri")]
    public string Uri { get; set; } = string.Empty;
}

internal sealed class LspPosition
{
    [JsonPropertyName("line")]
    public int Line { get; set; }

    [JsonPropertyName("character")]
    public int Character { get; set; }
}

internal class LspTextDocumentPositionParams
{
    [JsonPropertyName("textDocument")]
    public LspTextDocumentIdentifier TextDocument { get; set; } = new();

    [JsonPropertyName("position")]
    public LspPosition Position { get; set; } = new();
}

internal sealed class LspTextDocumentItem
{
    [JsonPropertyName("uri")]
    public string Uri { get; set; } = string.Empty;

    [JsonPropertyName("languageId")]
    public string LanguageId { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;
}

internal sealed class LspDidOpenTextDocumentParams
{
    [JsonPropertyName("textDocument")]
    public LspTextDocumentItem TextDocument { get; set; } = new();
}

internal sealed class LspDidCloseTextDocumentParams
{
    [JsonPropertyName("textDocument")]
    public LspTextDocumentIdentifier TextDocument { get; set; } = new();
}

internal sealed class LspReferenceParams : LspTextDocumentPositionParams
{
    [JsonPropertyName("context")]
    public LspReferenceContext Context { get; set; } = new();
}

internal sealed class LspReferenceContext
{
    [JsonPropertyName("includeDeclaration")]
    public bool IncludeDeclaration { get; set; } = true;
}

internal sealed class LspCallHierarchyItem
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("kind")]
    public int Kind { get; set; }

    [JsonPropertyName("detail")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Detail { get; set; }

    [JsonPropertyName("uri")]
    public string Uri { get; set; } = string.Empty;

    [JsonPropertyName("range")]
    public LspRange Range { get; set; } = new();

    [JsonPropertyName("selectionRange")]
    public LspRange SelectionRange { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

internal sealed class LspCallHierarchyIncomingCall
{
    [JsonPropertyName("from")]
    public LspCallHierarchyItem From { get; set; } = new();
}

internal sealed class LspCallHierarchyOutgoingCall
{
    [JsonPropertyName("to")]
    public LspCallHierarchyItem To { get; set; } = new();
}

internal sealed class LspLocation
{
    [JsonPropertyName("uri")]
    public string Uri { get; set; } = string.Empty;

    [JsonPropertyName("range")]
    public LspRange Range { get; set; } = new();
}

internal sealed class LspRange
{
    [JsonPropertyName("start")]
    public LspPosition Start { get; set; } = new();

    [JsonPropertyName("end")]
    public LspPosition End { get; set; } = new();
}
