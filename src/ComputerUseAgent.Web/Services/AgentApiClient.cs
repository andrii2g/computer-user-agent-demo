using System.Net.Http.Json;
using ComputerUseAgent.Core.Models;

namespace ComputerUseAgent.Web.Services;

public sealed class AgentApiClient
{
    private readonly HttpClient _httpClient;

    public AgentApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public string BaseUrl => _httpClient.BaseAddress?.ToString() ?? string.Empty;

    public async Task<SessionSummary> CreateSessionAsync(string prompt, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync("/api/sessions", new { prompt }, cancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SessionSummary>(cancellationToken))!;
    }

    public async Task<SessionSummary> GetSessionAsync(string id, CancellationToken cancellationToken)
    {
        return (await _httpClient.GetFromJsonAsync<SessionSummary>($"/api/sessions/{id}", cancellationToken))!;
    }

    public async Task<IReadOnlyList<SessionEvent>> GetEventsAsync(string id, CancellationToken cancellationToken)
    {
        return (await _httpClient.GetFromJsonAsync<List<SessionEvent>>($"/api/sessions/{id}/events", cancellationToken))!;
    }

    public async Task<IReadOnlyList<WorkspaceFileRecordDto>> GetFilesAsync(string id, CancellationToken cancellationToken)
    {
        return (await _httpClient.GetFromJsonAsync<List<WorkspaceFileRecordDto>>($"/api/sessions/{id}/files", cancellationToken))!;
    }

    public Task<string> GetFileContentAsync(string id, string path, CancellationToken cancellationToken)
    {
        return _httpClient.GetStringAsync($"/api/sessions/{id}/files/content?path={Uri.EscapeDataString(path)}", cancellationToken);
    }
}
