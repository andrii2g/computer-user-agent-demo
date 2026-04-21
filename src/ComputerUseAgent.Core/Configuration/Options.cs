namespace ComputerUseAgent.Core.Configuration;

public sealed class OpenAiOptions
{
    public string ApiKey { get; set; } = string.Empty;

    public string Model { get; set; } = "gpt-5.2";
}

public sealed class SandboxOptions
{
    public string Image { get; set; } = "computeruseagent-sandbox:local";

    public string WorkspaceRoot { get; set; } = "./data/sessions";

    public int CommandTimeoutSeconds { get; set; } = 30;

    public int MaxShellCommands { get; set; } = 20;

    public int MaxToolCalls { get; set; } = 30;

    public int MaxWorkspaceBytes { get; set; } = 5 * 1024 * 1024;

    public int MaxFileWriteBytes { get; set; } = 256 * 1024;

    public int MaxReadContentBytes { get; set; } = 128 * 1024;

    public int MaxReadAbsoluteBytes { get; set; } = 512 * 1024;

    public int MaxStdoutBytes { get; set; } = 128 * 1024;

    public int MaxStderrBytes { get; set; } = 128 * 1024;

    public int MaxRunDurationSeconds { get; set; } = 5 * 60;
}

public sealed class DatabaseOptions
{
    public string ConnectionString { get; set; } = "Data Source=./data/app.db";
}
