using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ComputerUseAgent.Core.Configuration;
using ComputerUseAgent.Core.Interfaces;
using ComputerUseAgent.Core.Models;
using ComputerUseAgent.Core.Tools;
using Microsoft.Extensions.Options;

namespace ComputerUseAgent.Infrastructure.OpenAI;

public sealed class OpenAiResponsesAgentClient : IResponsesAgentClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly OpenAiOptions _options;

    public OpenAiResponsesAgentClient(HttpClient httpClient, IOptions<OpenAiOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _httpClient.BaseAddress = new Uri("https://api.openai.com/");
    }

    public async Task<ModelTurnResult> GetNextTurnAsync(ResponsesAgentRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException("OpenAI API key is not configured.");
        }

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "v1/responses");
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        httpRequest.Content = new StringContent(
            JsonSerializer.Serialize(BuildPayload(request), JsonOptions),
            Encoding.UTF8,
            "application/json");

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"OpenAI Responses API request failed: {content}");
        }

        using var document = JsonDocument.Parse(content);
        var root = document.RootElement;
        var responseId = root.TryGetProperty("id", out var responseIdElement) ? responseIdElement.GetString() : null;
        var toolCalls = new List<ModelRequestedToolCall>();
        string? finalOutputText = null;

        if (root.TryGetProperty("output", out var outputArray) && outputArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in outputArray.EnumerateArray())
            {
                if (!item.TryGetProperty("type", out var typeElement))
                {
                    continue;
                }

                var type = typeElement.GetString();
                if (type == "function_call")
                {
                    toolCalls.Add(new ModelRequestedToolCall(
                        item.GetProperty("call_id").GetString() ?? Guid.NewGuid().ToString("N"),
                        item.GetProperty("name").GetString() ?? string.Empty,
                        item.GetProperty("arguments").GetString() ?? "{}"));
                }
                else if (type == "message" &&
                         item.TryGetProperty("content", out var contentArrayElement) &&
                         contentArrayElement.ValueKind == JsonValueKind.Array)
                {
                    finalOutputText = string.Join(
                        Environment.NewLine,
                        contentArrayElement.EnumerateArray()
                            .Where(contentItem => contentItem.TryGetProperty("type", out var contentType) && contentType.GetString() == "output_text")
                            .Select(contentItem => contentItem.GetProperty("text").GetString())
                            .Where(text => !string.IsNullOrWhiteSpace(text)));
                }
            }
        }

        return new ModelTurnResult(responseId, toolCalls, string.IsNullOrWhiteSpace(finalOutputText) ? null : finalOutputText);
    }

    private static Dictionary<string, object?> BuildPayload(ResponsesAgentRequest request)
    {
        var payload = new Dictionary<string, object?>
        {
            ["model"] = request.Model,
            ["instructions"] = request.SystemInstructions,
            ["tools"] = BuildTools()
        };

        if (!string.IsNullOrWhiteSpace(request.PreviousResponseId))
        {
            payload["previous_response_id"] = request.PreviousResponseId;
            payload["input"] = request.ToolOutputs.Select(output => new Dictionary<string, object?>
            {
                ["type"] = "function_call_output",
                ["call_id"] = output.CallId,
                ["output"] = output.OutputJson
            }).ToArray();
        }
        else
        {
            payload["input"] = request.InitialPrompt ?? string.Empty;
        }

        return payload;
    }

    private static object[] BuildTools()
    {
        return
        [
            new
            {
                type = "function",
                name = ToolNames.ListFiles,
                description = "Inspect workspace directory structure.",
                parameters = new
                {
                    type = "object",
                    properties = new
                    {
                        path = new { type = "string" }
                    },
                    required = new[] { "path" }
                }
            },
            new
            {
                type = "function",
                name = ToolNames.ReadFile,
                description = "Read a text file from the workspace.",
                parameters = new
                {
                    type = "object",
                    properties = new
                    {
                        path = new { type = "string" }
                    },
                    required = new[] { "path" }
                }
            },
            new
            {
                type = "function",
                name = ToolNames.WriteFile,
                description = "Create or overwrite a text file in the workspace.",
                parameters = new
                {
                    type = "object",
                    properties = new
                    {
                        path = new { type = "string" },
                        content = new { type = "string" }
                    },
                    required = new[] { "path", "content" }
                }
            },
            new
            {
                type = "function",
                name = ToolNames.RunShellCommand,
                description = "Run a validated shell command in the sandbox.",
                parameters = new
                {
                    type = "object",
                    properties = new
                    {
                        command = new { type = "string" },
                        working_directory = new { type = "string" },
                        timeout_seconds = new { type = "integer" }
                    },
                    required = new[] { "command", "working_directory", "timeout_seconds" }
                }
            },
            new
            {
                type = "function",
                name = ToolNames.FinishTask,
                description = "Declare completion when the task is finished.",
                parameters = new
                {
                    type = "object",
                    properties = new
                    {
                        summary = new { type = "string" },
                        output_files = new
                        {
                            type = "array",
                            items = new { type = "string" }
                        }
                    },
                    required = new[] { "summary", "output_files" }
                }
            }
        ];
    }
}
