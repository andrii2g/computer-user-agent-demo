using ComputerUseAgent.Core.Models;
using ComputerUseAgent.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ComputerUseAgent.Web.Pages.Sessions;

public sealed class DetailsModel : PageModel
{
    private readonly AgentApiClient _apiClient;

    public DetailsModel(AgentApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    [FromRoute]
    public string Id { get; set; } = string.Empty;

    public SessionSummary? Summary { get; private set; }

    public IReadOnlyList<WorkspaceFileRecordDto> Files { get; private set; } = Array.Empty<WorkspaceFileRecordDto>();

    public IReadOnlyList<SessionEvent> Events { get; private set; } = Array.Empty<SessionEvent>();

    public Dictionary<string, string> Previews { get; } = new(StringComparer.Ordinal);

    public string? ErrorMessage { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        try
        {
            Summary = await _apiClient.GetSessionAsync(Id, cancellationToken);
            Files = await _apiClient.GetFilesAsync(Id, cancellationToken);
            Events = await _apiClient.GetEventsAsync(Id, cancellationToken);

            foreach (var file in Files.Take(5))
            {
                try
                {
                    Previews[file.Path] = await _apiClient.GetFileContentAsync(Id, file.Path, cancellationToken);
                }
                catch
                {
                    // Ignore preview failures for unavailable or non-text files.
                }
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    public string GetFileUrl(string path, bool download)
    {
        return $"{_apiClient.BaseUrl}api/sessions/{Id}/files/content?path={Uri.EscapeDataString(path)}&download={download.ToString().ToLowerInvariant()}";
    }
}
