using System.Text;
using ComputerUseAgent.Core.Configuration;
using ComputerUseAgent.Core.Interfaces;
using ComputerUseAgent.Core.Models;
using ComputerUseAgent.Core.Tools;
using Microsoft.Extensions.Options;

namespace ComputerUseAgent.Infrastructure.Workspace;

public sealed class LocalWorkspaceService : IWorkspaceService
{
    private readonly SandboxOptions _options;
    private readonly IPathPolicy _pathPolicy;

    public LocalWorkspaceService(IOptions<SandboxOptions> options, IPathPolicy pathPolicy)
    {
        _options = options.Value;
        _pathPolicy = pathPolicy;
    }

    public Task<string> CreateWorkspaceAsync(string sessionId, CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(Path.Combine(_options.WorkspaceRoot, sessionId, "workspace"));
        Directory.CreateDirectory(root);
        return Task.FromResult(root);
    }

    public Task<ListFilesResponse> ListFilesAsync(string workspaceRoot, string path, CancellationToken cancellationToken)
    {
        var resolution = Resolve(workspaceRoot, path);
        var entries = new List<ListFilesEntry>();

        foreach (var directoryPath in Directory.GetDirectories(resolution.ResolvedPath!, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            entries.Add(new ListFilesEntry("directory", Path.GetRelativePath(workspaceRoot, directoryPath).Replace('\\', '/'), null));
        }

        foreach (var filePath in Directory.GetFiles(resolution.ResolvedPath!, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = new FileInfo(filePath);
            entries.Add(new ListFilesEntry("file", Path.GetRelativePath(workspaceRoot, filePath).Replace('\\', '/'), info.Length));
        }

        return Task.FromResult(new ListFilesResponse(path, entries.OrderBy(entry => entry.Path, StringComparer.Ordinal).ToArray()));
    }

    public async Task<ReadFileResponse> ReadTextFileAsync(
        string workspaceRoot,
        string path,
        int responseContentBytes,
        int? absoluteSafetyCeilingBytes,
        CancellationToken cancellationToken)
    {
        var resolution = Resolve(workspaceRoot, path);
        if (!File.Exists(resolution.ResolvedPath))
        {
            throw new FileNotFoundException($"File '{path}' was not found.");
        }

        var info = new FileInfo(resolution.ResolvedPath!);
        if (absoluteSafetyCeilingBytes is not null && info.Length > absoluteSafetyCeilingBytes.Value)
        {
            throw new InvalidOperationException("File exceeds the configured absolute safety ceiling.");
        }

        var bytes = await File.ReadAllBytesAsync(resolution.ResolvedPath!, cancellationToken);
        if (LooksBinary(bytes))
        {
            throw new InvalidOperationException("Binary-like files cannot be read through read_file.");
        }

        var truncated = bytes.Length > responseContentBytes;
        var selectedBytes = truncated ? bytes[..responseContentBytes] : bytes;
        return new ReadFileResponse(
            resolution.RelativePath!,
            Encoding.UTF8.GetString(selectedBytes),
            truncated);
    }

    public async Task<WriteFileResponse> WriteTextFileAsync(
        string workspaceRoot,
        string path,
        string content,
        int maxWriteBytes,
        int maxWorkspaceBytes,
        CancellationToken cancellationToken)
    {
        var resolution = Resolve(workspaceRoot, path);
        var bytes = Encoding.UTF8.GetBytes(content);
        if (bytes.Length > maxWriteBytes)
        {
            throw new InvalidOperationException("File content exceeds the maximum write size.");
        }

        var directory = Path.GetDirectoryName(resolution.ResolvedPath!);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllBytesAsync(resolution.ResolvedPath!, bytes, cancellationToken);

        var totalSize = Directory.GetFiles(workspaceRoot, "*", SearchOption.AllDirectories)
            .Sum(filePath => new FileInfo(filePath).Length);
        if (totalSize > maxWorkspaceBytes)
        {
            File.Delete(resolution.ResolvedPath!);
            throw new InvalidOperationException("Workspace exceeds the configured maximum size.");
        }

        return new WriteFileResponse(resolution.RelativePath!, bytes.Length);
    }

    public Task<IReadOnlyList<WorkspaceFileRecord>> SnapshotFilesAsync(
        string sessionId,
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        var records = new List<WorkspaceFileRecord>();
        foreach (var filePath in Directory.GetFiles(workspaceRoot, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = new FileInfo(filePath);
            records.Add(new WorkspaceFileRecord(
                sessionId,
                Path.GetRelativePath(workspaceRoot, filePath).Replace('\\', '/'),
                info.Length,
                new DateTimeOffset(info.CreationTimeUtc),
                new DateTimeOffset(info.LastWriteTimeUtc)));
        }

        return Task.FromResult<IReadOnlyList<WorkspaceFileRecord>>(records.OrderBy(record => record.RelativePath, StringComparer.Ordinal).ToArray());
    }

    private PathResolutionResult Resolve(string workspaceRoot, string path)
    {
        var resolution = _pathPolicy.ResolvePath(workspaceRoot, path);
        if (!resolution.Success)
        {
            throw new InvalidOperationException(resolution.Error);
        }

        return resolution;
    }

    private static bool LooksBinary(byte[] bytes)
    {
        var sampleLength = Math.Min(bytes.Length, 1024);
        for (var index = 0; index < sampleLength; index++)
        {
            if (bytes[index] == 0)
            {
                return true;
            }
        }

        return false;
    }
}
