using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Serialization;
using Fleet.Cli.Services;

namespace Fleet.Cli.Commands;

/// <summary>
/// 'audit' command — view the audit log from the Cloud Console.
/// </summary>
public static class AuditCommand
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public static Command Create(CliConfigManager configManager)
    {
        var lastOption = new Option<int>("--last") { Description = "Number of recent events to show", DefaultValueFactory = _ => 20 };
        var agentOption = new Option<string?>("--agent") { Description = "Filter by agent ID" };
        var jsonOption = new Option<bool>("--json") { Description = "Output as JSON" };

        var command = new Command("audit", "View audit log");
        command.Add(lastOption);
        command.Add(agentOption);
        command.Add(jsonOption);

        command.SetAction(async (parseResult, ct) =>
        {
            var last = parseResult.GetValue(lastOption);
            var agentFilter = parseResult.GetValue(agentOption);
            var json = parseResult.GetValue(jsonOption);

            var config = configManager.EnsureConfigured(await configManager.LoadAsync());
            using var client = new CloudApiClient(config);

            var events = await client.GetAuditLogAsync();

            if (!string.IsNullOrWhiteSpace(agentFilter))
                events = events.Where(e => e.AgentId == agentFilter).ToList();

            events = events.Take(last).ToList();

            if (json)
            {
                Console.WriteLine(JsonSerializer.Serialize(events, s_jsonOptions));
                return;
            }

            Console.WriteLine($"{"TIMESTAMP",-22} {"AGENT",-28} {"CATEGORY",-14} {"MESSAGE"}");
            Console.WriteLine(new string('-', 90));
            foreach (var e in events)
            {
                Console.WriteLine($"{e.TimestampUtc:u,-22} {e.AgentId,-28} {e.Category,-14} {e.Message}");
            }
        });

        return command;
    }
}
