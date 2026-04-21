using ComputerUseAgent.Core.Configuration;
using ComputerUseAgent.Core.Interfaces;
using ComputerUseAgent.Core.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace ComputerUseAgent.Infrastructure.Persistence;

public sealed class SqliteSessionRepository : ISessionRepository
{
    private readonly DatabaseOptions _options;

    public SqliteSessionRepository(IOptions<DatabaseOptions> options)
    {
        _options = options.Value;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var builder = new SqliteConnectionStringBuilder(_options.ConnectionString);
        if (!string.IsNullOrWhiteSpace(builder.DataSource))
        {
            var fullPath = Path.GetFullPath(builder.DataSource);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS sessions (
                id TEXT PRIMARY KEY,
                prompt TEXT NOT NULL,
                status TEXT NOT NULL,
                created_utc TEXT NOT NULL,
                updated_utc TEXT NOT NULL,
                final_answer TEXT NULL,
                failure_reason TEXT NULL,
                host_workspace_path TEXT NOT NULL,
                container_workspace_path TEXT NOT NULL,
                sandbox_container_id TEXT NULL
            );

            CREATE TABLE IF NOT EXISTS events (
                id TEXT PRIMARY KEY,
                session_id TEXT NOT NULL,
                sequence INTEGER NOT NULL,
                event_type TEXT NOT NULL,
                timestamp_utc TEXT NOT NULL,
                payload_json TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_events_session_sequence
            ON events(session_id, sequence);

            CREATE TABLE IF NOT EXISTS files (
                id TEXT PRIMARY KEY,
                session_id TEXT NOT NULL,
                relative_path TEXT NOT NULL,
                size_bytes INTEGER NOT NULL,
                created_utc TEXT NOT NULL,
                updated_utc TEXT NOT NULL,
                UNIQUE(session_id, relative_path)
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task CreateSessionAsync(AgentSession session, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO sessions (
                id, prompt, status, created_utc, updated_utc, final_answer, failure_reason,
                host_workspace_path, container_workspace_path, sandbox_container_id
            ) VALUES (
                $id, $prompt, $status, $created, $updated, $final, $failure, $hostWorkspace, $containerWorkspace, $containerId
            );
            """;
        BindSession(command, session);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateSessionAsync(AgentSession session, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE sessions
            SET prompt = $prompt,
                status = $status,
                created_utc = $created,
                updated_utc = $updated,
                final_answer = $final,
                failure_reason = $failure,
                host_workspace_path = $hostWorkspace,
                container_workspace_path = $containerWorkspace,
                sandbox_container_id = $containerId
            WHERE id = $id;
            """;
        BindSession(command, session);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task AppendEventAsync(SessionEvent sessionEvent, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO events (id, session_id, sequence, event_type, timestamp_utc, payload_json)
            VALUES ($id, $sessionId, $sequence, $eventType, $timestamp, $payload);
            """;
        command.Parameters.AddWithValue("$id", sessionEvent.Id);
        command.Parameters.AddWithValue("$sessionId", sessionEvent.SessionId);
        command.Parameters.AddWithValue("$sequence", sessionEvent.Sequence);
        command.Parameters.AddWithValue("$eventType", sessionEvent.EventType);
        command.Parameters.AddWithValue("$timestamp", sessionEvent.TimestampUtc.UtcDateTime.ToString("O"));
        command.Parameters.AddWithValue("$payload", sessionEvent.PayloadJson);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<AgentSession?> GetSessionAsync(string sessionId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM sessions WHERE id = $id;";
        command.Parameters.AddWithValue("$id", sessionId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadSession(reader);
    }

    public async Task<SessionSummary?> GetSessionSummaryAsync(string sessionId, CancellationToken cancellationToken)
    {
        var session = await GetSessionAsync(sessionId, cancellationToken);
        if (session is null)
        {
            return null;
        }

        var files = await ListFilesAsync(sessionId, cancellationToken);
        return new SessionSummary(
            session.Id,
            session.Prompt,
            session.Status,
            session.FinalAnswer,
            session.CreatedUtc,
            session.UpdatedUtc,
            files.Select(file => new WorkspaceFileRecordDto(file.RelativePath, file.SizeBytes)).ToArray());
    }

    public async Task<IReadOnlyList<SessionEvent>> ListEventsAsync(string sessionId, CancellationToken cancellationToken)
    {
        var events = new List<SessionEvent>();
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT id, session_id, sequence, event_type, timestamp_utc, payload_json FROM events WHERE session_id = $sessionId ORDER BY sequence ASC;";
        command.Parameters.AddWithValue("$sessionId", sessionId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            events.Add(new SessionEvent(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.GetString(3),
                DateTimeOffset.Parse(reader.GetString(4)),
                reader.GetString(5)));
        }

        return events;
    }

    public async Task<IReadOnlyList<WorkspaceFileRecord>> ListFilesAsync(string sessionId, CancellationToken cancellationToken)
    {
        var files = new List<WorkspaceFileRecord>();
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT session_id, relative_path, size_bytes, created_utc, updated_utc FROM files WHERE session_id = $sessionId ORDER BY relative_path ASC;";
        command.Parameters.AddWithValue("$sessionId", sessionId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            files.Add(new WorkspaceFileRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt64(2),
                DateTimeOffset.Parse(reader.GetString(3)),
                DateTimeOffset.Parse(reader.GetString(4))));
        }

        return files;
    }

    public async Task UpsertFilesAsync(string sessionId, IReadOnlyList<WorkspaceFileRecord> files, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var dbTransaction = await connection.BeginTransactionAsync(cancellationToken);
        var transaction = (SqliteTransaction)dbTransaction;

        var deleteCommand = connection.CreateCommand();
        deleteCommand.Transaction = transaction;
        deleteCommand.CommandText = "DELETE FROM files WHERE session_id = $sessionId;";
        deleteCommand.Parameters.AddWithValue("$sessionId", sessionId);
        await deleteCommand.ExecuteNonQueryAsync(cancellationToken);

        foreach (var file in files)
        {
            var insertCommand = connection.CreateCommand();
            insertCommand.Transaction = transaction;
            insertCommand.CommandText =
                """
                INSERT INTO files (id, session_id, relative_path, size_bytes, created_utc, updated_utc)
                VALUES ($id, $sessionId, $relativePath, $sizeBytes, $created, $updated);
                """;
            insertCommand.Parameters.AddWithValue("$id", $"{sessionId}:{file.RelativePath}");
            insertCommand.Parameters.AddWithValue("$sessionId", sessionId);
            insertCommand.Parameters.AddWithValue("$relativePath", file.RelativePath);
            insertCommand.Parameters.AddWithValue("$sizeBytes", file.SizeBytes);
            insertCommand.Parameters.AddWithValue("$created", file.CreatedUtc.UtcDateTime.ToString("O"));
            insertCommand.Parameters.AddWithValue("$updated", file.UpdatedUtc.UtcDateTime.ToString("O"));
            await insertCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static void BindSession(SqliteCommand command, AgentSession session)
    {
        command.Parameters.AddWithValue("$id", session.Id);
        command.Parameters.AddWithValue("$prompt", session.Prompt);
        command.Parameters.AddWithValue("$status", session.Status.ToString());
        command.Parameters.AddWithValue("$created", session.CreatedUtc.UtcDateTime.ToString("O"));
        command.Parameters.AddWithValue("$updated", session.UpdatedUtc.UtcDateTime.ToString("O"));
        command.Parameters.AddWithValue("$final", (object?)session.FinalAnswer ?? DBNull.Value);
        command.Parameters.AddWithValue("$failure", (object?)session.FailureReason ?? DBNull.Value);
        command.Parameters.AddWithValue("$hostWorkspace", session.HostWorkspacePath);
        command.Parameters.AddWithValue("$containerWorkspace", session.ContainerWorkspacePath);
        command.Parameters.AddWithValue("$containerId", (object?)session.SandboxContainerId ?? DBNull.Value);
    }

    private static AgentSession ReadSession(SqliteDataReader reader)
    {
        return new AgentSession(
            reader.GetString(0),
            reader.GetString(1),
            Enum.Parse<AgentSessionStatus>(reader.GetString(2)),
            DateTimeOffset.Parse(reader.GetString(3)),
            DateTimeOffset.Parse(reader.GetString(4)),
            reader.GetString(7),
            reader.GetString(8),
            reader.IsDBNull(9) ? null : reader.GetString(9),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6));
    }
}
