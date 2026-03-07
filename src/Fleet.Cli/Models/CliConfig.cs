namespace Fleet.Cli.Models;

/// <summary>
/// Persisted CLI configuration (stored at ~/.config/fleet-ctl/config.json).
/// </summary>
public sealed record CliConfig(
    string ConsoleUrl = "",
    string ApiKey = "",
    string DefaultOutput = "table"
);
