using ComputerUseAgent.Core.Configuration;
using ComputerUseAgent.Core.Models;
using ComputerUseAgent.Core.Tools;

namespace ComputerUseAgent.Core.Interfaces;

public interface ISessionRepository
{
    Task InitializeAsync(CancellationToken cancellationToken);

    Task CreateSessionAsync(AgentSession session, CancellationToken cancellationToken);

    Task UpdateSessionAsync(AgentSession session, CancellationToken cancellationToken);

    Task AppendEventAsync(SessionEvent sessionEvent, CancellationToken cancellationToken);

    Task<AgentSession?> GetSessionAsync(string sessionId, CancellationToken cancellationToken);

    Task<SessionSummary?> GetSessionSummaryAsync(string sessionId, CancellationToken cancellationToken);

    Task<IReadOnlyList<SessionEvent>> ListEventsAsync(string sessionId, CancellationToken cancellationToken);

    Task<IReadOnlyList<WorkspaceFileRecord>> ListFilesAsync(string sessionId, CancellationToken cancellationToken);

    Task UpsertFilesAsync(string sessionId, IReadOnlyList<WorkspaceFileRecord> files, CancellationToken cancellationToken);
}

public interface ISandboxService
{
    Task<string> CreateSandboxAsync(AgentSession session, SandboxOptions options, CancellationToken cancellationToken);

    Task<ShellCommandResult> ExecuteCommandAsync(
        string containerId,
        string workingDirectory,
        string command,
        SandboxOptions options,
        CancellationToken cancellationToken);

    Task TeardownAsync(string? containerId, CancellationToken cancellationToken);
}

public interface IResponsesAgentClient
{
    Task<ModelTurnResult> GetNextTurnAsync(ResponsesAgentRequest request, CancellationToken cancellationToken);
}

public interface IWorkspaceService
{
    Task<string> CreateWorkspaceAsync(string sessionId, CancellationToken cancellationToken);

    Task<ListFilesResponse> ListFilesAsync(string workspaceRoot, string path, CancellationToken cancellationToken);

    Task<ReadFileResponse> ReadTextFileAsync(
        string workspaceRoot,
        string path,
        int responseContentBytes,
        int? absoluteSafetyCeilingBytes,
        CancellationToken cancellationToken);

    Task<WriteFileResponse> WriteTextFileAsync(
        string workspaceRoot,
        string path,
        string content,
        int maxWriteBytes,
        int maxWorkspaceBytes,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<WorkspaceFileRecord>> SnapshotFilesAsync(
        string sessionId,
        string workspaceRoot,
        CancellationToken cancellationToken);
}

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public interface IIdGenerator
{
    string CreateSessionId();

    string CreateEventId();
}

public interface ICommandPolicy
{
    PolicyDecision Evaluate(string command);
}

public interface IPathPolicy
{
    PathResolutionResult ResolvePath(string workspaceRoot, string requestedPath);
}
