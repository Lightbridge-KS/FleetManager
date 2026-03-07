using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Fleet.Cli.Models;
using Shared.Contracts.Messages;
using Shared.Contracts.Models;

namespace Fleet.Cli.Services;

/// <summary>
/// Thin HttpClient wrapper for the Cloud Console REST API.
/// </summary>
public sealed class CloudApiClient : IDisposable
{
    private readonly HttpClient _http;

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public CloudApiClient(CliConfig config)
    {
        _http = new HttpClient { BaseAddress = new Uri(config.ConsoleUrl.TrimEnd('/') + "/") };
        _http.DefaultRequestHeaders.Add("Accept", "application/json");

        if (!string.IsNullOrWhiteSpace(config.ApiKey))
            _http.DefaultRequestHeaders.Add("Authorization", $"Bearer {config.ApiKey}");
    }

    public async Task<List<AgentDto>> GetAgentsAsync()
    {
        var response = await _http.GetAsync("api/agents");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<AgentDto>>(s_jsonOptions) ?? [];
    }

    public async Task<AgentDto?> GetAgentAsync(string agentId)
    {
        var response = await _http.GetAsync($"api/agents/{Uri.EscapeDataString(agentId)}");
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AgentDto>(s_jsonOptions);
    }

    public async Task<List<AuditEvent>> GetAuditLogAsync()
    {
        var response = await _http.GetAsync("api/audit");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<AuditEvent>>(s_jsonOptions) ?? [];
    }

    public async Task<HttpResponseMessage> RestartAppAsync(string agentId, RestartAppCommand command)
        => await PostCommandAsync($"api/agents/{Uri.EscapeDataString(agentId)}/restart-app", command);

    public async Task<HttpResponseMessage> PushConfigAsync(string agentId, PushConfigCommand command)
        => await PostCommandAsync($"api/agents/{Uri.EscapeDataString(agentId)}/push-config", command);

    public async Task<HttpResponseMessage> UpdateAppAsync(string agentId, UpdateAppCommand command)
        => await PostCommandAsync($"api/agents/{Uri.EscapeDataString(agentId)}/update-app", command);

    public void Dispose() => _http.Dispose();

    private async Task<HttpResponseMessage> PostCommandAsync<T>(string path, T command)
        => await _http.PostAsJsonAsync(path, command, s_jsonOptions);
}
