using System.Diagnostics;
using System.Text;
using ComputerUseAgent.Core.Configuration;
using ComputerUseAgent.Core.Interfaces;
using ComputerUseAgent.Core.Models;

namespace ComputerUseAgent.Infrastructure.Sandboxing;

public sealed class DockerSandboxService : ISandboxService
{
    public async Task<string> CreateSandboxAsync(AgentSession session, SandboxOptions options, CancellationToken cancellationToken)
    {
        var containerName = $"cua-{session.Id[..Math.Min(session.Id.Length, 20)]}";
        var arguments = $"run -d --name {containerName} --workdir /workspace -v \"{session.HostWorkspacePath}:/workspace\" {options.Image} sleep infinity";
        var result = await RunProcessAsync("docker", arguments, options.CommandTimeoutSeconds + 10, cancellationToken);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"Failed to create sandbox container: {result.Stderr}");
        }

        return result.Stdout.Trim();
    }

    public async Task<ShellCommandResult> ExecuteCommandAsync(
        string containerId,
        string workingDirectory,
        string command,
        SandboxOptions options,
        CancellationToken cancellationToken)
    {
        var escapedCommand = command.Replace("\"", "\\\"");
        var arguments = $"exec -w {workingDirectory} {containerId} sh -lc \"timeout {options.CommandTimeoutSeconds}s sh -lc \\\"{escapedCommand}\\\"\"";
        var stopwatch = Stopwatch.StartNew();
        var result = await RunProcessAsync("docker", arguments, options.CommandTimeoutSeconds + 10, cancellationToken);
        stopwatch.Stop();

        return new ShellCommandResult(
            command,
            workingDirectory,
            result.ExitCode,
            Truncate(result.Stdout, options.MaxStdoutBytes),
            Truncate(result.Stderr, options.MaxStderrBytes),
            result.ExitCode == 124,
            stopwatch.ElapsedMilliseconds);
    }

    public async Task TeardownAsync(string? containerId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(containerId))
        {
            return;
        }

        try
        {
            await RunProcessAsync("docker", $"rm -f {containerId}", 15, cancellationToken);
        }
        catch
        {
            // Best effort cleanup.
        }
    }

    private static async Task<ProcessResult> RunProcessAsync(string fileName, string arguments, int timeoutSeconds, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                stdout.AppendLine(args.Data);
            }
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                stderr.AppendLine(args.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Ignore best effort kill failures.
            }

            throw new InvalidOperationException("Process execution timed out.");
        }

        return new ProcessResult(process.ExitCode, stdout.ToString(), stderr.ToString());
    }

    private static string Truncate(string value, int maxBytes)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length <= maxBytes)
        {
            return value;
        }

        return Encoding.UTF8.GetString(bytes[..maxBytes]);
    }

    private sealed record ProcessResult(int ExitCode, string Stdout, string Stderr);
}
