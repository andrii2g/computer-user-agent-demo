using ComputerUseAgent.Core.Configuration;
using ComputerUseAgent.Core.Models;
using ComputerUseAgent.Core.Orchestration;
using ComputerUseAgent.Infrastructure.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<OpenAiOptions>(builder.Configuration.GetSection("OpenAI"));
builder.Services.Configure<SandboxOptions>(builder.Configuration.GetSection("Sandbox"));
builder.Services.Configure<DatabaseOptions>(builder.Configuration.GetSection("Database"));
builder.Services.AddComputerUseAgent();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var repository = scope.ServiceProvider.GetRequiredService<ComputerUseAgent.Core.Interfaces.ISessionRepository>();
    await repository.InitializeAsync(CancellationToken.None);
}

app.MapPost("/api/sessions", async (
    CreateSessionRequest request,
    AgentRunOrchestrator orchestrator,
    Microsoft.Extensions.Options.IOptions<OpenAiOptions> openAiOptions,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Prompt))
    {
        return Results.BadRequest(new { error = "Prompt is required." });
    }

    var summary = await orchestrator.RunAsync(request.Prompt.Trim(), openAiOptions.Value, cancellationToken);
    return Results.Ok(summary);
});

app.MapGet("/api/sessions/{id}", async (
    string id,
    ComputerUseAgent.Core.Interfaces.ISessionRepository repository,
    CancellationToken cancellationToken) =>
{
    var summary = await repository.GetSessionSummaryAsync(id, cancellationToken);
    return summary is null ? Results.NotFound() : Results.Ok(summary);
});

app.MapGet("/api/sessions/{id}/events", async (
    string id,
    ComputerUseAgent.Core.Interfaces.ISessionRepository repository,
    CancellationToken cancellationToken) =>
{
    var session = await repository.GetSessionAsync(id, cancellationToken);
    if (session is null)
    {
        return Results.NotFound();
    }

    var events = await repository.ListEventsAsync(id, cancellationToken);
    return Results.Ok(events);
});

app.MapGet("/api/sessions/{id}/files", async (
    string id,
    ComputerUseAgent.Core.Interfaces.ISessionRepository repository,
    CancellationToken cancellationToken) =>
{
    var session = await repository.GetSessionAsync(id, cancellationToken);
    if (session is null)
    {
        return Results.NotFound();
    }

    var files = await repository.ListFilesAsync(id, cancellationToken);
    return Results.Ok(files.Select(file => new WorkspaceFileRecordDto(file.RelativePath, file.SizeBytes)));
});

app.MapGet("/api/sessions/{id}/files/content", async (
    string id,
    string path,
    bool? download,
    ComputerUseAgent.Core.Interfaces.ISessionRepository repository,
    ComputerUseAgent.Core.Interfaces.IWorkspaceService workspaceService,
    Microsoft.Extensions.Options.IOptions<SandboxOptions> sandboxOptions,
    CancellationToken cancellationToken) =>
{
    var session = await repository.GetSessionAsync(id, cancellationToken);
    if (session is null)
    {
        return Results.NotFound();
    }

    var file = await workspaceService.ReadTextFileAsync(
        session.HostWorkspacePath,
        path,
        sandboxOptions.Value.MaxReadAbsoluteBytes,
        sandboxOptions.Value.MaxReadAbsoluteBytes,
        cancellationToken);

    var fileBytes = System.Text.Encoding.UTF8.GetBytes(file.Content);
    return download == true
        ? Results.File(fileBytes, "text/plain; charset=utf-8", file.Path)
        : Results.Text(file.Content, "text/plain; charset=utf-8");
});

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));

app.Run();

public sealed record CreateSessionRequest(string Prompt);
