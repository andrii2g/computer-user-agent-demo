using System.Text.Json.Serialization;

namespace ComputerUseAgent.Core.Tools;

public static class ToolNames
{
    public const string ListFiles = "list_files";
    public const string ReadFile = "read_file";
    public const string WriteFile = "write_file";
    public const string RunShellCommand = "run_shell_command";
    public const string FinishTask = "finish_task";

    public static readonly IReadOnlyList<string> All =
    [
        ListFiles,
        ReadFile,
        WriteFile,
        RunShellCommand,
        FinishTask
    ];
}

public sealed record ListFilesRequest(
    [property: JsonPropertyName("path")] string Path);

public sealed record ListFilesResponse(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("entries")] IReadOnlyList<ListFilesEntry> Entries);

public sealed record ListFilesEntry(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("size")] long? Size);

public sealed record ReadFileRequest(
    [property: JsonPropertyName("path")] string Path);

public sealed record ReadFileResponse(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("content")] string Content,
    [property: JsonPropertyName("truncated")] bool Truncated);

public sealed record WriteFileRequest(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("content")] string Content);

public sealed record WriteFileResponse(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("bytes_written")] int BytesWritten);

public sealed record RunShellCommandRequest(
    [property: JsonPropertyName("command")] string Command,
    [property: JsonPropertyName("working_directory")] string WorkingDirectory,
    [property: JsonPropertyName("timeout_seconds")] int TimeoutSeconds);

public sealed record RunShellCommandResponse(
    [property: JsonPropertyName("command")] string Command,
    [property: JsonPropertyName("working_directory")] string WorkingDirectory,
    [property: JsonPropertyName("exit_code")] int ExitCode,
    [property: JsonPropertyName("stdout")] string Stdout,
    [property: JsonPropertyName("stderr")] string Stderr,
    [property: JsonPropertyName("timed_out")] bool TimedOut,
    [property: JsonPropertyName("duration_ms")] long DurationMs);

public sealed record FinishTaskRequest(
    [property: JsonPropertyName("summary")] string Summary,
    [property: JsonPropertyName("output_files")] IReadOnlyList<string> OutputFiles);

public sealed record FinishTaskResponse(
    [property: JsonPropertyName("accepted")] bool Accepted);

public sealed record ToolErrorResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("error_code")] string ErrorCode,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("recoverable")] bool Recoverable);
