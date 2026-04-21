using System.Text.RegularExpressions;
using ComputerUseAgent.Core.Interfaces;
using ComputerUseAgent.Core.Models;

namespace ComputerUseAgent.Core.Policies;

public sealed class DefaultCommandPolicy : ICommandPolicy
{
    private static readonly string[] AllowedPrefixes =
    [
        "python",
        "python3",
        "bash",
        "sh",
        "ls",
        "cat",
        "echo",
        "pwd",
        "mkdir",
        "find",
        "grep",
        "sed",
        "awk",
        "head",
        "tail",
        "wc"
    ];

    private static readonly string[] BlockedPatterns =
    [
        "sudo",
        "su ",
        "curl",
        "wget",
        "apt",
        "apt-get",
        "yum",
        "apk",
        "dnf",
        "pip install",
        "npm install",
        "git clone",
        "ssh",
        "scp",
        "nc ",
        "netcat",
        "telnet",
        "docker",
        "kubectl",
        "chmod 777",
        "chown",
        "/etc/",
        "/proc/",
        "/sys/",
        "..",
        "&&",
        "||",
        ";",
        "|",
        ">",
        ">>",
        "<",
        "`",
        "$(",
        "&",
        "nohup",
        "rm -rf /",
        "mkfs",
        "dd ",
        "mount"
    ];

    public PolicyDecision Evaluate(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return new PolicyDecision(false, "Command cannot be empty.");
        }

        if (command.Contains('\n') || command.Contains('\r'))
        {
            return new PolicyDecision(false, "Multiline commands are not allowed.");
        }

        var trimmed = command.Trim();
        var firstToken = Regex.Split(trimmed, "\\s+")[0];
        if (!AllowedPrefixes.Contains(firstToken, StringComparer.Ordinal))
        {
            return new PolicyDecision(false, $"Executable prefix '{firstToken}' is not allowed.");
        }

        foreach (var blockedPattern in BlockedPatterns)
        {
            if (trimmed.Contains(blockedPattern, StringComparison.Ordinal))
            {
                return new PolicyDecision(false, $"Command contains blocked pattern '{blockedPattern}'.");
            }
        }

        return new PolicyDecision(true);
    }
}

public sealed class DefaultPathPolicy : IPathPolicy
{
    public PathResolutionResult ResolvePath(string workspaceRoot, string requestedPath)
    {
        var candidate = string.IsNullOrWhiteSpace(requestedPath) ? "." : requestedPath;
        candidate = candidate.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(candidate))
        {
            return new PathResolutionResult(false, null, null, "Absolute host paths are not allowed.");
        }

        var workspaceFullPath = Path.GetFullPath(workspaceRoot);
        var resolved = Path.GetFullPath(Path.Combine(workspaceFullPath, candidate));

        if (!resolved.StartsWith(workspaceFullPath, StringComparison.OrdinalIgnoreCase))
        {
            return new PathResolutionResult(false, null, null, "Path escapes the workspace root.");
        }

        var relative = Path.GetRelativePath(workspaceFullPath, resolved).Replace('\\', '/');
        if (relative == ".")
        {
            relative = ".";
        }

        return new PathResolutionResult(true, resolved, relative, null);
    }
}

public static class FinishTaskValidator
{
    public static PolicyDecision ValidateOutputFiles(
        IReadOnlyList<string> outputFiles,
        IReadOnlyList<WorkspaceFileRecord> existingFiles)
    {
        var paths = new HashSet<string>(existingFiles.Select(file => file.RelativePath), StringComparer.Ordinal);
        foreach (var outputFile in outputFiles)
        {
            if (!paths.Contains(outputFile))
            {
                return new PolicyDecision(false, $"Output file '{outputFile}' does not exist.");
            }
        }

        return new PolicyDecision(true);
    }
}
