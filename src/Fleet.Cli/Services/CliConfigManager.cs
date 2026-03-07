using System.Text.Json;
using Fleet.Cli.Models;

namespace Fleet.Cli.Services;

/// <summary>
/// Manages reading/writing the CLI configuration file at ~/.config/fleet-ctl/config.json.
/// </summary>
public sealed class CliConfigManager
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public string ConfigPath { get; }

    public CliConfigManager()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        ConfigPath = Path.Combine(home, ".config", "fleet-ctl", "config.json");
    }

    public async Task<CliConfig> LoadAsync()
    {
        if (!File.Exists(ConfigPath))
            return new CliConfig();

        var json = await File.ReadAllTextAsync(ConfigPath);
        return JsonSerializer.Deserialize<CliConfig>(json, s_jsonOptions) ?? new CliConfig();
    }

    public async Task SaveAsync(CliConfig config)
    {
        var dir = Path.GetDirectoryName(ConfigPath)!;
        Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(config, s_jsonOptions);
        await File.WriteAllTextAsync(ConfigPath, json);

        // Set file mode 600 on Unix systems for security
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(ConfigPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    public CliConfig EnsureConfigured(CliConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.ConsoleUrl))
        {
            Console.Error.WriteLine("Not configured. Run 'fleet-ctl login' first.");
            Environment.Exit(1);
        }
        return config;
    }
}
