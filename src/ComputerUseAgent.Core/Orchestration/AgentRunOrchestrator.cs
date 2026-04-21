using System.Diagnostics;
using System.Text.Json;
using ComputerUseAgent.Core.Configuration;
using ComputerUseAgent.Core.Interfaces;
using ComputerUseAgent.Core.Models;
using ComputerUseAgent.Core.Policies;
using ComputerUseAgent.Core.Tools;

namespace ComputerUseAgent.Core.Orchestration;

public sealed class AgentRunOrchestrator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly ISessionRepository _sessionRepository;
    private readonly ISandboxService _sandboxService;
    private readonly IResponsesAgentClient _responsesAgentClient;
    private readonly IWorkspaceService _workspaceService;
    private readonly IClock _clock;
    private readonly IIdGenerator _idGenerator;
    private readonly ICommandPolicy _commandPolicy;
    private readonly SandboxOptions _sandboxOptions;

    public AgentRunOrchestrator(
        ISessionRepository sessionRepository,
        ISandboxService sandboxService,
        IResponsesAgentClient responsesAgentClient,
        IWorkspaceService workspaceService,
        IClock clock,
        IIdGenerator idGenerator,
        ICommandPolicy commandPolicy,
        SandboxOptions sandboxOptions)
    {
        _sessionRepository = sessionRepository;
        _sandboxService = sandboxService;
        _responsesAgentClient = responsesAgentClient;
        _workspaceService = workspaceService;
        _clock = clock;
        _idGenerator = idGenerator;
        _commandPolicy = commandPolicy;
        _sandboxOptions = sandboxOptions;
    }

    public async Task<SessionSummary> RunAsync(string prompt, OpenAiOptions openAiOptions, CancellationToken cancellationToken)
    {
        await _sessionRepository.InitializeAsync(cancellationToken);

        var startedAt = _clock.UtcNow;
        var sessionId = _idGenerator.CreateSessionId();
        var workspaceRoot = await _workspaceService.CreateWorkspaceAsync(sessionId, cancellationToken);
        var session = new AgentSession(
            sessionId,
            prompt,
            AgentSessionStatus.Created,
            startedAt,
            startedAt,
            workspaceRoot,
            "/workspace",
            null,
            null,
            null);

        await _sessionRepository.CreateSessionAsync(session, cancellationToken);
        await AppendEventAsync(sessionId, "session_created", new { prompt }, cancellationToken);

        string? previousResponseId = null;
        var pendingToolOutputs = new List<ToolCallOutput>();
        var totalToolCalls = 0;
        var shellCommands = 0;

        try
        {
            var containerId = await _sandboxService.CreateSandboxAsync(session, _sandboxOptions, cancellationToken);
            session = session with
            {
                SandboxContainerId = containerId,
                Status = AgentSessionStatus.Running,
                UpdatedUtc = _clock.UtcNow
            };
            await _sessionRepository.UpdateSessionAsync(session, cancellationToken);
            await AppendEventAsync(sessionId, "sandbox_created", new { containerId }, cancellationToken);

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if ((_clock.UtcNow - startedAt).TotalSeconds > _sandboxOptions.MaxRunDurationSeconds)
                {
                    session = await FailSessionAsync(session, "Maximum run duration exceeded.", cancellationToken);
                    break;
                }

                var agentRequest = new ResponsesAgentRequest(
                    openAiOptions.Model,
                    BuildSystemInstructions(),
                    previousResponseId is null ? prompt : null,
                    previousResponseId,
                    pendingToolOutputs.ToArray());

                pendingToolOutputs.Clear();

                var modelTurn = await _responsesAgentClient.GetNextTurnAsync(agentRequest, cancellationToken);
                previousResponseId = modelTurn.ResponseId ?? previousResponseId;
                await AppendEventAsync(
                    sessionId,
                    "model_requested",
                    new
                    {
                        responseId = previousResponseId,
                        toolCalls = modelTurn.ToolCalls.Count,
                        hasFinalOutput = !string.IsNullOrWhiteSpace(modelTurn.FinalOutputText)
                    },
                    cancellationToken);

                if (modelTurn.ToolCalls.Count == 0)
                {
                    if (!string.IsNullOrWhiteSpace(modelTurn.FinalOutputText))
                    {
                        session = session with
                        {
                            Status = AgentSessionStatus.Completed,
                            FinalAnswer = modelTurn.FinalOutputText,
                            UpdatedUtc = _clock.UtcNow
                        };
                        await _sessionRepository.UpdateSessionAsync(session, cancellationToken);
                        await AppendEventAsync(sessionId, "session_completed", new { finalAnswer = session.FinalAnswer }, cancellationToken);
                        break;
                    }

                    session = await FailSessionAsync(session, "Model returned neither tool calls nor final output.", cancellationToken);
                    break;
                }

                foreach (var toolCall in modelTurn.ToolCalls)
                {
                    totalToolCalls++;
                    await AppendEventAsync(sessionId, "tool_call_requested", new { toolCall.CallId, toolCall.Name, toolCall.ArgumentsJson }, cancellationToken);

                    if (totalToolCalls > _sandboxOptions.MaxToolCalls)
                    {
                        session = await FailSessionAsync(session, "Maximum tool call limit exceeded.", cancellationToken);
                        break;
                    }

                    var toolResult = await ExecuteToolCallAsync(
                        session,
                        toolCall,
                        ref shellCommands,
                        cancellationToken);

                    await AppendEventAsync(
                        sessionId,
                        toolResult.Success ? "tool_call_completed" : "tool_call_rejected",
                        new
                        {
                            toolCall.CallId,
                            toolCall.Name,
                            toolResult.Success,
                            toolResult.ErrorCode,
                            toolResult.ErrorMessage,
                            toolResult.Recoverable,
                            toolResult.ResultJson
                        },
                        cancellationToken);

                    pendingToolOutputs.Add(new ToolCallOutput(toolCall.CallId, toolResult.ResultJson));

                    if (toolCall.Name == ToolNames.FinishTask && toolResult.Success)
                    {
                        var finishRequest = JsonSerializer.Deserialize<FinishTaskRequest>(toolCall.ArgumentsJson, JsonOptions)!;
                        session = session with
                        {
                            Status = AgentSessionStatus.Completed,
                            FinalAnswer = finishRequest.Summary,
                            UpdatedUtc = _clock.UtcNow
                        };
                        await _sessionRepository.UpdateSessionAsync(session, cancellationToken);
                        await AppendEventAsync(sessionId, "session_completed", new { finalAnswer = finishRequest.Summary }, cancellationToken);
                        break;
                    }
                }

                if (session.Status is AgentSessionStatus.Completed or AgentSessionStatus.Failed or AgentSessionStatus.Blocked)
                {
                    break;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            session = await FailSessionAsync(session, ex.Message, cancellationToken);
        }
        finally
        {
            await _sandboxService.TeardownAsync(session.SandboxContainerId, cancellationToken);
        }

        return (await _sessionRepository.GetSessionSummaryAsync(session.Id, cancellationToken))!;
    }

    private async Task<ToolExecutionResult> ExecuteToolCallAsync(
        AgentSession session,
        ModelRequestedToolCall toolCall,
        ref int shellCommands,
        CancellationToken cancellationToken)
    {
        try
        {
            return toolCall.Name switch
            {
                ToolNames.ListFiles => await HandleListFilesAsync(session, toolCall.ArgumentsJson, cancellationToken),
                ToolNames.ReadFile => await HandleReadFileAsync(session, toolCall.ArgumentsJson, cancellationToken),
                ToolNames.WriteFile => await HandleWriteFileAsync(session, toolCall.ArgumentsJson, cancellationToken),
                ToolNames.RunShellCommand => await HandleRunShellCommandAsync(session, toolCall.ArgumentsJson, ref shellCommands, cancellationToken),
                ToolNames.FinishTask => await HandleFinishTaskAsync(session, toolCall.ArgumentsJson, cancellationToken),
                _ => CreateToolError(toolCall.Name, "unsupported_tool", $"Unsupported tool '{toolCall.Name}'.", false)
            };
        }
        catch (JsonException ex)
        {
            return CreateToolError(toolCall.Name, "invalid_arguments", ex.Message, true);
        }
        catch (Exception ex)
        {
            return CreateToolError(toolCall.Name, "tool_execution_failed", ex.Message, true);
        }
    }

    private async Task<ToolExecutionResult> HandleListFilesAsync(AgentSession session, string argumentsJson, CancellationToken cancellationToken)
    {
        var request = JsonSerializer.Deserialize<ListFilesRequest>(argumentsJson, JsonOptions)!;
        var response = await _workspaceService.ListFilesAsync(session.HostWorkspacePath, request.Path, cancellationToken);
        return CreateToolSuccess(ToolNames.ListFiles, response);
    }

    private async Task<ToolExecutionResult> HandleReadFileAsync(AgentSession session, string argumentsJson, CancellationToken cancellationToken)
    {
        var request = JsonSerializer.Deserialize<ReadFileRequest>(argumentsJson, JsonOptions)!;
        var response = await _workspaceService.ReadTextFileAsync(
            session.HostWorkspacePath,
            request.Path,
            _sandboxOptions.MaxReadContentBytes,
            _sandboxOptions.MaxReadAbsoluteBytes,
            cancellationToken);
        return CreateToolSuccess(ToolNames.ReadFile, response);
    }

    private async Task<ToolExecutionResult> HandleWriteFileAsync(AgentSession session, string argumentsJson, CancellationToken cancellationToken)
    {
        var request = JsonSerializer.Deserialize<WriteFileRequest>(argumentsJson, JsonOptions)!;
        var response = await _workspaceService.WriteTextFileAsync(
            session.HostWorkspacePath,
            request.Path,
            request.Content,
            _sandboxOptions.MaxFileWriteBytes,
            _sandboxOptions.MaxWorkspaceBytes,
            cancellationToken);
        await RefreshFilesAsync(session, cancellationToken);
        return CreateToolSuccess(ToolNames.WriteFile, response);
    }

    private async Task<ToolExecutionResult> HandleRunShellCommandAsync(
        AgentSession session,
        string argumentsJson,
        ref int shellCommands,
        CancellationToken cancellationToken)
    {
        if (++shellCommands > _sandboxOptions.MaxShellCommands)
        {
            return CreateToolError(ToolNames.RunShellCommand, "shell_limit_exceeded", "Maximum shell command limit exceeded.", false);
        }

        var request = JsonSerializer.Deserialize<RunShellCommandRequest>(argumentsJson, JsonOptions)!;
        if (request.TimeoutSeconds > _sandboxOptions.CommandTimeoutSeconds)
        {
            return CreateToolError(ToolNames.RunShellCommand, "timeout_exceeded", "Requested timeout exceeds the configured maximum.", true);
        }

        var decision = _commandPolicy.Evaluate(request.Command);
        if (!decision.Allowed)
        {
            return CreateToolError(ToolNames.RunShellCommand, "command_rejected", decision.Reason ?? "Command rejected by policy.", true);
        }

        var containerWorkingDirectory = request.WorkingDirectory switch
        {
            "." or "" => session.ContainerWorkspacePath,
            _ => $"{session.ContainerWorkspacePath.TrimEnd('/')}/{request.WorkingDirectory.TrimStart('/').Replace('\\', '/')}"
        };

        var result = await _sandboxService.ExecuteCommandAsync(
            session.SandboxContainerId!,
            containerWorkingDirectory,
            request.Command,
            _sandboxOptions with { CommandTimeoutSeconds = request.TimeoutSeconds },
            cancellationToken);

        var response = new RunShellCommandResponse(
            result.Command,
            request.WorkingDirectory,
            result.ExitCode,
            result.Stdout,
            result.Stderr,
            result.TimedOut,
            result.DurationMs);

        await RefreshFilesAsync(session, cancellationToken);
        return CreateToolSuccess(ToolNames.RunShellCommand, response);
    }

    private async Task<ToolExecutionResult> HandleFinishTaskAsync(AgentSession session, string argumentsJson, CancellationToken cancellationToken)
    {
        var request = JsonSerializer.Deserialize<FinishTaskRequest>(argumentsJson, JsonOptions)!;
        var existingFiles = await _sessionRepository.ListFilesAsync(session.Id, cancellationToken);
        var validation = FinishTaskValidator.ValidateOutputFiles(request.OutputFiles, existingFiles);
        if (!validation.Allowed)
        {
            return CreateToolError(ToolNames.FinishTask, "missing_output_file", validation.Reason ?? "Referenced output file is missing.", true);
        }

        return CreateToolSuccess(ToolNames.FinishTask, new FinishTaskResponse(true));
    }

    private async Task RefreshFilesAsync(AgentSession session, CancellationToken cancellationToken)
    {
        var files = await _workspaceService.SnapshotFilesAsync(session.Id, session.HostWorkspacePath, cancellationToken);
        await _sessionRepository.UpsertFilesAsync(session.Id, files, cancellationToken);
    }

    private ToolExecutionResult CreateToolSuccess<T>(string toolName, T payload)
    {
        return new ToolExecutionResult(
            toolName,
            true,
            JsonSerializer.Serialize(payload, JsonOptions));
    }

    private static ToolExecutionResult CreateToolError(string toolName, string code, string message, bool recoverable)
    {
        var payload = new ToolErrorResponse(false, code, message, recoverable);
        return new ToolExecutionResult(
            toolName,
            false,
            JsonSerializer.Serialize(payload, JsonOptions),
            code,
            message,
            recoverable);
    }

    private async Task<AgentSession> FailSessionAsync(AgentSession session, string reason, CancellationToken cancellationToken)
    {
        var failed = session with
        {
            Status = AgentSessionStatus.Failed,
            FailureReason = reason,
            UpdatedUtc = _clock.UtcNow
        };
        await _sessionRepository.UpdateSessionAsync(failed, cancellationToken);
        await AppendEventAsync(session.Id, "session_failed", new { reason }, cancellationToken);
        return failed;
    }

    private async Task AppendEventAsync(string sessionId, string eventType, object payload, CancellationToken cancellationToken)
    {
        var existingEvents = await _sessionRepository.ListEventsAsync(sessionId, cancellationToken);
        var sessionEvent = new SessionEvent(
            _idGenerator.CreateEventId(),
            sessionId,
            existingEvents.Count + 1,
            eventType,
            _clock.UtcNow,
            JsonSerializer.Serialize(payload, JsonOptions));
        await _sessionRepository.AppendEventAsync(sessionEvent, cancellationToken);
    }

    private static string BuildSystemInstructions()
    {
        return """
You are an agent operating in a controlled sandbox.
You cannot directly execute code or inspect files without using tools.
Prefer small, verifiable steps.
Before running code, create or inspect the relevant files.
After running a shell command, inspect stdout/stderr and decide next action.
If a command fails, fix the root cause instead of repeating blindly.
Only use paths inside the workspace.
Do not attempt network access, package installation, privilege escalation, or background daemons.
Do not pretend a command ran when no tool call occurred.
Do not reference nonexistent files as outputs.
Do not use unsupported tools.
Do not attempt unsafe shell operations.
Do not infinite retry.
Do not write massive files.
Do not rely on internet or external downloads.
Prefer python file.py, ls, cat, pwd, echo, mkdir -p.
All file creation and file updates should be done with write_file, not shell redirection.
Finish only when the requested task is fully complete.
When complete, call finish_task.
""";
    }
}
