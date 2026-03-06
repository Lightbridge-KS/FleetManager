using Shared.Contracts.Models;

namespace Agent.Worker.Services;

/// <summary>
/// Simulates the local app management layer.
/// In production, this would interact with systemd (systemctl), file system, etc.
/// 
/// Responsibilities:
///   - Track installed apps and their status
///   - Restart apps (systemctl restart ...)
///   - Apply config changes (write appsettings.json)
///   - Download and swap binaries for updates
/// </summary>
public sealed class LocalAppManager
{
    private readonly List<LocalApp> _apps;
    private readonly ILogger<LocalAppManager> _logger;

    public LocalAppManager(ILogger<LocalAppManager> logger)
    {
        _logger = logger;

        // Simulate two pre-installed applications
        _apps =
        [
            new LocalApp("radiology-ai",  "Radiology AI Service",  "1.2.0", AppStatus.Running),
            new LocalApp("dicom-gateway",  "DICOM Gateway",         "3.0.1", AppStatus.Running)
        ];
    }

    /// <summary>
    /// Snapshot of currently managed apps (for heartbeat reporting).
    /// </summary>
    public List<ManagedApp> GetManagedApps()
        => _apps.Select(a => new ManagedApp(a.AppId, a.Name, a.Version, a.Status)).ToList();

    /// <summary>
    /// Simulate restarting an app (in production: systemctl restart {unit}).
    /// </summary>
    public async Task<bool> RestartAppAsync(string appId)
    {
        var app = _apps.FirstOrDefault(a => a.AppId == appId);
        if (app is null) return false;

        _logger.LogInformation("Restarting app {AppId}...", appId);
        app.Status = AppStatus.Stopped;
        await Task.Delay(1500);  // simulate restart time
        app.Status = AppStatus.Running;
        _logger.LogInformation("App {AppId} restarted successfully", appId);
        return true;
    }

    /// <summary>
    /// Simulate applying new configuration (in production: write appsettings.json + restart).
    /// </summary>
    public Task<bool> ApplyConfigAsync(string appId, Dictionary<string, string> settings)
    {
        var app = _apps.FirstOrDefault(a => a.AppId == appId);
        if (app is null) return Task.FromResult(false);

        foreach (var (key, value) in settings)
        {
            _logger.LogInformation("Config [{AppId}]: {Key} = {Value}", appId, key, value);
            app.Config[key] = value;
        }
        return Task.FromResult(true);
    }

    /// <summary>
    /// Simulate updating an app to a new version.
    /// In production: download artifact → stop → backup → swap → start → health check → rollback on fail.
    /// </summary>
    public async Task<bool> UpdateAppAsync(string appId, string targetVersion, string artifactUrl)
    {
        var app = _apps.FirstOrDefault(a => a.AppId == appId);
        if (app is null) return false;

        _logger.LogInformation("Updating {AppId}: {Old} → {New} (from {Url})",
            appId, app.Version, targetVersion, artifactUrl);

        app.Status = AppStatus.Updating;
        await Task.Delay(3000);  // simulate download + swap

        app.Version = targetVersion;
        app.Status = AppStatus.Running;
        _logger.LogInformation("App {AppId} updated to {Version}", appId, targetVersion);
        return true;
    }

    // ──────────────────────── Internal State ────────────────────────

    private sealed class LocalApp(string appId, string name, string version, AppStatus status)
    {
        public string AppId { get; } = appId;
        public string Name { get; } = name;
        public string Version { get; set; } = version;
        public AppStatus Status { get; set; } = status;
        public Dictionary<string, string> Config { get; } = new();
    }
}
