using ComputerUseAgent.Core.Configuration;
using ComputerUseAgent.Core.Models;
using ComputerUseAgent.Infrastructure.Persistence;
using Microsoft.Extensions.Options;

namespace ComputerUseAgent.Tests;

public sealed class RepositoryTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"cua-db-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task RepositoryInitializesAndStoresSession()
    {
        var repository = CreateRepository();
        await repository.InitializeAsync(CancellationToken.None);

        var session = new AgentSession(
            "sess_test",
            "prompt",
            AgentSessionStatus.Created,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            "C:/workspace",
            "/workspace",
            null,
            null,
            null);

        await repository.CreateSessionAsync(session, CancellationToken.None);
        var loaded = await repository.GetSessionAsync("sess_test", CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(session.Prompt, loaded!.Prompt);
    }

    private SqliteSessionRepository CreateRepository()
    {
        return new SqliteSessionRepository(Options.Create(new DatabaseOptions
        {
            ConnectionString = $"Data Source={_dbPath}"
        }));
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }
}
