using System.Text.Json.Serialization;

namespace ComputerUseAgent.Core.Models;

public enum AgentSessionStatus
{
    Created,
    Running,
    Completed,
    Failed,
    Blocked
}

public sealed record AgentSession(
    string Id,
    string Prompt,
    AgentSessionStatus Status,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc,
    string HostWorkspacePath,
    string ContainerWorkspacePath,
    string? SandboxContainerId,
    string? FinalAnswer,
    string? FailureReason);

public sealed record SessionEvent(
    string Id,
    string SessionId,
    int Sequence,
    string EventType,
    DateTimeOffset TimestampUtc,
    string PayloadJson);

public sealed record WorkspaceFileRecord(
    string SessionId,
    string RelativePath,
    long SizeBytes,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc);

public sealed record SessionSummary(
    string Id,
    string Prompt,
    AgentSessionStatus Status,
    string? FinalAnswer,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc,
    IReadOnlyList<WorkspaceFileRecordDto> Files);

public sealed record WorkspaceFileRecordDto(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("sizeBytes")] long SizeBytes);

public sealed record PolicyDecision(bool Allowed, string? Reason = null);

public sealed record ToolExecutionResult(
    string ToolName,
    bool Success,
    string ResultJson,
    string? ErrorCode = null,
    string? ErrorMessage = null,
    bool Recoverable = false);

public sealed record ShellCommandResult(
    string Command,
    string WorkingDirectory,
    int ExitCode,
    string Stdout,
    string Stderr,
    bool TimedOut,
    long DurationMs);

public sealed record ModelRequestedToolCall(
    string CallId,
    string Name,
    string ArgumentsJson);

public sealed record ModelTurnResult(
    string? ResponseId,
    IReadOnlyList<ModelRequestedToolCall> ToolCalls,
    string? FinalOutputText);

public sealed record ToolCallOutput(
    string CallId,
    string OutputJson);

public sealed record ResponsesAgentRequest(
    string Model,
    string SystemInstructions,
    string? InitialPrompt,
    string? PreviousResponseId,
    IReadOnlyList<ToolCallOutput> ToolOutputs);

public sealed record PathResolutionResult(
    bool Success,
    string? ResolvedPath,
    string? RelativePath,
    string? Error);
