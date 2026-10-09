using System.Text.Json;

namespace GoogleDocsLauncher;

internal class LauncherConfig {
    public string GoogleDriveRoot { get; set; } = "";
    public int RetryIntervalMs { get; set; } = 250;

    public static LauncherConfig Load() {
        string path = Path.Combine(AppContext.BaseDirectory, "config.json");
        string json = File.ReadAllText(path);

        LauncherConfig config = JsonSerializer.Deserialize<LauncherConfig>(
            json,
            new JsonSerializerOptions {
                PropertyNameCaseInsensitive = true
            }
        ) ?? throw new InvalidOperationException("Invalid configuration.");

        if (string.IsNullOrWhiteSpace(config.GoogleDriveRoot)) {
            throw new InvalidOperationException("GoogleDriveRoot is required.");
        }

        if (config.RetryIntervalMs < 100) {
            throw new InvalidOperationException("RetryIntervalMs must be at least 100.");
        }

        config.GoogleDriveRoot = Environment.ExpandEnvironmentVariables(config.GoogleDriveRoot);

        return config;
    }
}