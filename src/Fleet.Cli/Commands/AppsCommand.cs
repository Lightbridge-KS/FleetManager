using System.CommandLine;
using Fleet.Cli.Services;
using Shared.Contracts.Messages;

namespace Fleet.Cli.Commands;

/// <summary>
/// 'apps' command group — restart, push config, and update managed applications.
/// </summary>
public static class AppsCommand
{
    public static Command Create(CliConfigManager configManager)
    {
        var command = new Command("apps", "Manage applications on agents");
        command.Add(CreateRestartCommand(configManager));
        command.Add(CreateConfigCommand(configManager));
        command.Add(CreateUpdateCommand(configManager));
        return command;
    }

    private static Command CreateRestartCommand(CliConfigManager configManager)
    {
        var agentIdArg = new Argument<string>("agentId") { Description = "Agent identifier" };
        var appIdArg = new Argument<string>("appId") { Description = "Application identifier" };

        var command = new Command("restart", "Restart an application");
        command.Add(agentIdArg);
        command.Add(appIdArg);

        command.SetAction(async (parseResult, ct) =>
        {
            var agentId = parseResult.GetValue(agentIdArg)!;
            var appId = parseResult.GetValue(appIdArg)!;

            var config = configManager.EnsureConfigured(await configManager.LoadAsync());
            using var client = new CloudApiClient(config);

            var response = await client.RestartAppAsync(agentId, new RestartAppCommand(appId));
            if (response.IsSuccessStatusCode)
                Console.WriteLine($"Restart command sent for '{appId}' on agent '{agentId}'.");
            else
                await PrintErrorAsync(response);
        });

        return command;
    }

    private static Command CreateConfigCommand(CliConfigManager configManager)
    {
        var agentIdArg = new Argument<string>("agentId") { Description = "Agent identifier" };
        var appIdArg = new Argument<string>("appId") { Description = "Application identifier" };
        var setOption = new Option<string[]>("--set") { Description = "Key=Value pair to set", Required = true, AllowMultipleArgumentsPerToken = true };

        var command = new Command("config", "Push configuration to an application");
        command.Add(agentIdArg);
        command.Add(appIdArg);
        command.Add(setOption);

        command.SetAction(async (parseResult, ct) =>
        {
            var agentId = parseResult.GetValue(agentIdArg)!;
            var appId = parseResult.GetValue(appIdArg)!;
            var setPairs = parseResult.GetValue(setOption) ?? [];

            var settings = new Dictionary<string, string>();
            foreach (var pair in setPairs)
            {
                var eqIndex = pair.IndexOf('=');
                if (eqIndex <= 0)
                {
                    Console.Error.WriteLine($"Invalid setting format: '{pair}'. Expected Key=Value.");
                    Environment.Exit(1);
                    return;
                }
                settings[pair[..eqIndex]] = pair[(eqIndex + 1)..];
            }

            var config = configManager.EnsureConfigured(await configManager.LoadAsync());
            using var client = new CloudApiClient(config);

            var response = await client.PushConfigAsync(agentId, new PushConfigCommand(appId, settings));
            if (response.IsSuccessStatusCode)
                Console.WriteLine($"Config pushed for '{appId}' on agent '{agentId}' ({settings.Count} setting(s)).");
            else
                await PrintErrorAsync(response);
        });

        return command;
    }

    private static Command CreateUpdateCommand(CliConfigManager configManager)
    {
        var agentIdArg = new Argument<string>("agentId") { Description = "Agent identifier" };
        var appIdArg = new Argument<string>("appId") { Description = "Application identifier" };
        var versionOption = new Option<string>("--version") { Description = "Target version", Required = true };
        var artifactOption = new Option<string>("--artifact") { Description = "Artifact download URL", Required = true };

        var command = new Command("update", "Update an application");
        command.Add(agentIdArg);
        command.Add(appIdArg);
        command.Add(versionOption);
        command.Add(artifactOption);

        command.SetAction(async (parseResult, ct) =>
        {
            var agentId = parseResult.GetValue(agentIdArg)!;
            var appId = parseResult.GetValue(appIdArg)!;
            var version = parseResult.GetValue(versionOption)!;
            var artifact = parseResult.GetValue(artifactOption)!;

            var config = configManager.EnsureConfigured(await configManager.LoadAsync());
            using var client = new CloudApiClient(config);

            var response = await client.UpdateAppAsync(agentId, new UpdateAppCommand(appId, version, artifact));
            if (response.IsSuccessStatusCode)
                Console.WriteLine($"Update command sent for '{appId}' on agent '{agentId}' -> v{version}.");
            else
                await PrintErrorAsync(response);
        });

        return command;
    }

    private static async Task PrintErrorAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Console.Error.WriteLine($"Error {(int)response.StatusCode}: {body}");
        Environment.Exit(1);
    }
}
