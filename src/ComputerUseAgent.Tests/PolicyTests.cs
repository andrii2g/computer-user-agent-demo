using ComputerUseAgent.Core.Policies;

namespace ComputerUseAgent.Tests;

public sealed class PolicyTests
{
    private readonly DefaultPathPolicy _pathPolicy = new();
    private readonly DefaultCommandPolicy _commandPolicy = new();

    [Fact]
    public void PathPolicyRejectsTraversal()
    {
        var result = _pathPolicy.ResolvePath("C:\\workspace", "..\\secret.txt");
        Assert.False(result.Success);
    }

    [Fact]
    public void PathPolicyAcceptsRelativePath()
    {
        var result = _pathPolicy.ResolvePath("C:\\workspace", "data\\report.md");
        Assert.True(result.Success);
        Assert.Equal("data/report.md", result.RelativePath);
    }

    [Fact]
    public void CommandPolicyAcceptsAllowedPrefix()
    {
        var result = _commandPolicy.Evaluate("python hello.py");
        Assert.True(result.Allowed);
    }

    [Fact]
    public void CommandPolicyRejectsBlockedPattern()
    {
        var result = _commandPolicy.Evaluate("python hello.py && echo done");
        Assert.False(result.Allowed);
    }
}
