using System.CommandLine;
using Fleet.Cli.Models;
using Fleet.Cli.Services;

namespace Fleet.Cli.Commands;

/// <summary>
/// 'login' command — configures the CLI with the Cloud Console URL and optional API key.
/// </summary>
public static class LoginCommand
{
    public static Command Create(CliConfigManager configManager)
    {
        var urlOption = new Option<string>("--url") { Description = "Cloud Console base URL", Required = true };
        var apiKeyOption = new Option<string?>("--api-key") { Description = "API key for authentication" };

        var command = new Command("login", "Connect to a Fleet Manager Cloud Console");
        command.Add(urlOption);
        command.Add(apiKeyOption);

        command.SetAction(async (parseResult, ct) =>
        {
            var url = parseResult.GetValue(urlOption)!;
            var apiKey = parseResult.GetValue(apiKeyOption);

            // Validate URL format
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
                || (uri.Scheme != "http" && uri.Scheme != "https"))
            {
                Console.Error.WriteLine($"Invalid URL: {url}");
                Environment.Exit(1);
                return;
            }

            // Prompt for API key interactively if not provided
            apiKey ??= PromptForApiKey();

            // Test connectivity
            var testConfig = new CliConfig(url, apiKey ?? "", "table");
            using var client = new CloudApiClient(testConfig);
            try
            {
                await client.GetAgentsAsync();
            }
            catch (HttpRequestException ex)
            {
                Console.Error.WriteLine($"Cannot connect to {url}: {ex.Message}");
                Environment.Exit(1);
                return;
            }

            // Save config
            await configManager.SaveAsync(testConfig);
            Console.WriteLine($"Logged in to {url} successfully.");
        });

        return command;
    }

    private static string? PromptForApiKey()
    {
        Console.Write("API Key (press Enter to skip): ");
        var key = ReadMasked();
        Console.WriteLine();
        return string.IsNullOrWhiteSpace(key) ? "" : key;
    }

    private static string ReadMasked()
    {
        var input = new System.Text.StringBuilder();
        while (true)
        {
            var keyInfo = Console.ReadKey(intercept: true);
            if (keyInfo.Key == ConsoleKey.Enter)
                break;
            if (keyInfo.Key == ConsoleKey.Backspace && input.Length > 0)
            {
                input.Remove(input.Length - 1, 1);
                Console.Write("\b \b");
            }
            else if (!char.IsControl(keyInfo.KeyChar))
            {
                input.Append(keyInfo.KeyChar);
                Console.Write('*');
            }
        }
        return input.ToString();
    }
}
