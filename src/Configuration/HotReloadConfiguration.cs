namespace TemporalWorkerApp.Configuration;

public class HotReloadConfiguration
{
    public bool Enabled { get; set; } = true;
    public HotReloadMode Mode { get; set; } = HotReloadMode.FileSystem;
    public FileSystemWatchingOptions? FileSystemWatching { get; set; }
    public ArtifactoryFeedWatchingOptions? ArtifactoryFeedWatching { get; set; }
}

public enum HotReloadMode
{
    FileSystem,
    ArtifactoryFeed,
    Both
}

public class FileSystemWatchingOptions
{
    public List<string> WatchPaths { get; set; } = new();
    public string FileFilter { get; set; } = "*.dll";
    public int DebounceDelayMs { get; set; } = 1000;
}

public class ArtifactoryFeedWatchingOptions
{
    public string FeedUrl { get; set; } = string.Empty;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public int PollIntervalSeconds { get; set; } = 30;
    public List<string> PackageFilters { get; set; } = new();
    public int CleanupOldPackagesHours { get; set; } = 24;
    public bool DownloadPackages { get; set; } = true;
    public string DownloadPath { get; set; } = Path.Combine(Path.GetTempPath(), "TemporalWorker", "FeedPackages");
}

public static class HotReloadConfigurationExtensions
{
    public static HotReloadConfiguration LoadFromEnvironment(this HotReloadConfiguration config)
    {
        // General settings
        if (bool.TryParse(Environment.GetEnvironmentVariable("HOT_RELOAD_ENABLED"), out var enabled))
        {
            config.Enabled = enabled;
        }

        if (Enum.TryParse<HotReloadMode>(Environment.GetEnvironmentVariable("HOT_RELOAD_MODE"), true, out var mode))
        {
            config.Mode = mode;
        }

        // Artifactory feed settings
        var feedUrl = Environment.GetEnvironmentVariable("ARTIFACTORY_FEED_URL");
        if (!string.IsNullOrEmpty(feedUrl))
        {
            config.ArtifactoryFeedWatching ??= new ArtifactoryFeedWatchingOptions();
            config.ArtifactoryFeedWatching.FeedUrl = feedUrl;
            config.ArtifactoryFeedWatching.Username = Environment.GetEnvironmentVariable("ARTIFACTORY_USERNAME");
            config.ArtifactoryFeedWatching.Password = Environment.GetEnvironmentVariable("ARTIFACTORY_PASSWORD");

            if (int.TryParse(Environment.GetEnvironmentVariable("ARTIFACTORY_POLL_INTERVAL_SECONDS"), out var pollInterval))
            {
                config.ArtifactoryFeedWatching.PollIntervalSeconds = pollInterval;
            }

            var packageFilters = Environment.GetEnvironmentVariable("ARTIFACTORY_PACKAGE_FILTERS");
            if (!string.IsNullOrEmpty(packageFilters))
            {
                config.ArtifactoryFeedWatching.PackageFilters = packageFilters
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(f => f.Trim())
                    .ToList();
            }

            var downloadPath = Environment.GetEnvironmentVariable("ARTIFACTORY_DOWNLOAD_PATH");
            if (!string.IsNullOrEmpty(downloadPath))
            {
                config.ArtifactoryFeedWatching.DownloadPath = downloadPath;
            }
        }

        // File system settings
        config.FileSystemWatching ??= new FileSystemWatchingOptions();
        
        var watchPaths = Environment.GetEnvironmentVariable("HOT_RELOAD_WATCH_PATHS");
        if (!string.IsNullOrEmpty(watchPaths))
        {
            config.FileSystemWatching.WatchPaths = watchPaths
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .ToList();
        }

        var fileFilter = Environment.GetEnvironmentVariable("HOT_RELOAD_FILE_FILTER");
        if (!string.IsNullOrEmpty(fileFilter))
        {
            config.FileSystemWatching.FileFilter = fileFilter;
        }

        if (int.TryParse(Environment.GetEnvironmentVariable("HOT_RELOAD_DEBOUNCE_MS"), out var debounceMs))
        {
            config.FileSystemWatching.DebounceDelayMs = debounceMs;
        }

        return config;
    }

    public static bool IsArtifactoryFeedConfigured(this HotReloadConfiguration config)
    {
        return config.ArtifactoryFeedWatching != null && 
               !string.IsNullOrEmpty(config.ArtifactoryFeedWatching.FeedUrl);
    }

    public static bool IsFileSystemWatchingConfigured(this HotReloadConfiguration config)
    {
        return config.FileSystemWatching != null;
    }
}