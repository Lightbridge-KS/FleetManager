using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Serialization;
using Fleet.Cli.Models;
using Fleet.Cli.Services;

namespace Fleet.Cli.Commands;

/// <summary>
/// 'agents' command group — list agents and show agent status.
/// </summary>
public static class AgentsCommand
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public static Command Create(CliConfigManager configManager)
    {
        var command = new Command("agents", "Manage fleet agents");
        command.Add(CreateListCommand(configManager));
        command.Add(CreateStatusCommand(configManager));
        return command;
    }

    private static Command CreateListCommand(CliConfigManager configManager)
    {
        var jsonOption = new Option<bool>("--json") { Description = "Output as JSON" };
        var onlineOnlyOption = new Option<bool>("--online-only") { Description = "Show only online agents" };

        var command = new Command("list", "List all agents");
        command.Add(jsonOption);
        command.Add(onlineOnlyOption);

        command.SetAction(async (parseResult, ct) =>
        {
            var json = parseResult.GetValue(jsonOption);
            var onlineOnly = parseResult.GetValue(onlineOnlyOption);

            var config = configManager.EnsureConfigured(await configManager.LoadAsync());
            using var client = new CloudApiClient(config);
            var agents = await client.GetAgentsAsync();

            if (onlineOnly)
                agents = agents.Where(a => a.IsOnline).ToList();

            if (json)
            {
                Console.WriteLine(JsonSerializer.Serialize(agents, s_jsonOptions));
                return;
            }

            // Table output
            Console.WriteLine($"{"AGENT ID",-28} {"HOSTNAME",-20} {"STATUS",-8} {"CPU",-7} {"MEM (MB)",-10} {"APPS",-5}");
            Console.WriteLine(new string('-', 82));
            foreach (var a in agents)
            {
                var status = a.IsOnline ? "Online" : "Offline";
                var cpu = a.IsOnline ? $"{a.CpuPercent:F1}%" : "-";
                var mem = a.IsOnline ? $"{a.MemoryUsedMb:F0}" : "-";
                Console.WriteLine($"{a.AgentId,-28} {a.Hostname,-20} {status,-8} {cpu,-7} {mem,-10} {a.ManagedApps.Count,-5}");
            }
        });

        return command;
    }

    private static Command CreateStatusCommand(CliConfigManager configManager)
    {
        var agentIdArg = new Argument<string>("agentId") { Description = "Agent identifier" };
        var jsonOption = new Option<bool>("--json") { Description = "Output as JSON" };

        var command = new Command("status", "Show detailed agent status");
        command.Add(agentIdArg);
        command.Add(jsonOption);

        command.SetAction(async (parseResult, ct) =>
        {
            var agentId = parseResult.GetValue(agentIdArg)!;
            var json = parseResult.GetValue(jsonOption);

            var config = configManager.EnsureConfigured(await configManager.LoadAsync());
            using var client = new CloudApiClient(config);
            var agent = await client.GetAgentAsync(agentId);

            if (agent is null)
            {
                Console.Error.WriteLine($"Agent '{agentId}' not found.");
                Environment.Exit(1);
                return;
            }

            if (json)
            {
                Console.WriteLine(JsonSerializer.Serialize(agent, s_jsonOptions));
                return;
            }

            // Detailed view
            Console.WriteLine($"Agent:       {agent.AgentId}");
            Console.WriteLine($"Hostname:    {agent.Hostname}");
            Console.WriteLine($"OS:          {agent.OsDescription}");
            Console.WriteLine($"Status:      {(agent.IsOnline ? "Online" : "Offline")}");
            Console.WriteLine($"Last Seen:   {agent.LastSeen:u}");
            Console.WriteLine();

            if (agent.IsOnline)
            {
                Console.WriteLine("── Metrics ──");
                Console.WriteLine($"CPU:         {agent.CpuPercent:F1}%");
                Console.WriteLine($"Memory:      {agent.MemoryUsedMb:F0} MB");
                Console.WriteLine($"Disk:        {agent.DiskUsedPercent:F1}%");
                Console.WriteLine();
            }

            Console.WriteLine("── Managed Apps ──");
            Console.WriteLine($"{"APP ID",-24} {"NAME",-20} {"VERSION",-12} {"STATUS",-10}");
            Console.WriteLine(new string('-', 68));
            foreach (var app in agent.ManagedApps)
            {
                Console.WriteLine($"{app.AppId,-24} {app.Name,-20} {app.Version,-12} {app.Status,-10}");
            }
        });

        return command;
    }
}
