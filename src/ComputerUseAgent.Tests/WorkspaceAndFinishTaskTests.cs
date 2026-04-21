using ComputerUseAgent.Core.Configuration;
using ComputerUseAgent.Core.Models;
using ComputerUseAgent.Core.Policies;
using ComputerUseAgent.Infrastructure.Workspace;
using Microsoft.Extensions.Options;

namespace ComputerUseAgent.Tests;

public sealed class WorkspaceAndFinishTaskTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"cua-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task WorkspaceWriteEnforcesSizeLimit()
    {
        Directory.CreateDirectory(_root);
        var service = CreateWorkspaceService();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.WriteTextFileAsync(_root, "big.txt", new string('a', 5000), 32, 1024, CancellationToken.None));
    }

    [Fact]
    public void FinishTaskValidationRejectsMissingOutput()
    {
        var result = FinishTaskValidator.ValidateOutputFiles(
            ["missing.txt"],
            [new WorkspaceFileRecord("sess", "hello.py", 10, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)]);

        Assert.False(result.Allowed);
    }

    [Fact]
    public async Task WorkspaceReadTruncatesLargeText()
    {
        Directory.CreateDirectory(_root);
        var service = CreateWorkspaceService();
        await File.WriteAllTextAsync(Path.Combine(_root, "large.txt"), new string('x', 64));

        var result = await service.ReadTextFileAsync(_root, "large.txt", 16, 128, CancellationToken.None);

        Assert.True(result.Truncated);
        Assert.Equal(16, result.Content.Length);
    }

    private LocalWorkspaceService CreateWorkspaceService()
    {
        return new LocalWorkspaceService(
            Options.Create(new SandboxOptions()),
            new DefaultPathPolicy());
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
