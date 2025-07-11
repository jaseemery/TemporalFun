using Microsoft.Extensions.Logging;
using TemporalWorkerApp.Configuration;
using TemporalWorkerApp.Managers;
using TemporalWorkerApp.Watchers;

namespace TemporalWorkerApp.Factories;

public static class HotReloadManagerFactory
{
    public static HotReloadManager Create(
        HotReloadConfiguration config,
        ILogger<HotReloadManager> logger,
        ILogger<PackageWatcher> packageWatcherLogger,
        ILogger<ArtifactoryFeedWatcher> feedWatcherLogger)
    {
        if (!config.Enabled)
        {
            throw new InvalidOperationException("Hot reload is disabled in configuration");
        }

        return config.Mode switch
        {
            HotReloadMode.FileSystem => CreateFileSystemWatcher(logger, packageWatcherLogger),
            HotReloadMode.ArtifactoryFeed => CreateArtifactoryFeedWatcher(config, logger, feedWatcherLogger),
            HotReloadMode.Both => CreateHybridWatcher(config, logger, packageWatcherLogger, feedWatcherLogger),
            _ => throw new ArgumentOutOfRangeException(nameof(config.Mode), config.Mode, "Invalid hot reload mode")
        };
    }

    private static HotReloadManager CreateFileSystemWatcher(
        ILogger<HotReloadManager> logger,
        ILogger<PackageWatcher> packageWatcherLogger)
    {
        return new HotReloadManager(logger, packageWatcherLogger);
    }

    private static HotReloadManager CreateArtifactoryFeedWatcher(
        HotReloadConfiguration config,
        ILogger<HotReloadManager> logger,
        ILogger<ArtifactoryFeedWatcher> feedWatcherLogger)
    {
        var feedConfig = config.ArtifactoryFeedWatching;
        if (feedConfig == null || string.IsNullOrEmpty(feedConfig.FeedUrl))
        {
            throw new InvalidOperationException("Artifactory feed configuration is missing or incomplete");
        }

        return new HotReloadManager(
            logger,
            feedWatcherLogger,
            feedConfig.FeedUrl,
            feedConfig.Username,
            feedConfig.Password,
            TimeSpan.FromSeconds(feedConfig.PollIntervalSeconds),
            feedConfig.PackageFilters);
    }

    private static HotReloadManager CreateHybridWatcher(
        HotReloadConfiguration config,
        ILogger<HotReloadManager> logger,
        ILogger<PackageWatcher> packageWatcherLogger,
        ILogger<ArtifactoryFeedWatcher> feedWatcherLogger)
    {
        // For now, prioritize Artifactory feed if configured, fall back to file system
        if (config.IsArtifactoryFeedConfigured())
        {
            logger.LogInformation("Using Artifactory feed watcher for hybrid mode");
            return CreateArtifactoryFeedWatcher(config, logger, feedWatcherLogger);
        }
        else
        {
            logger.LogInformation("Using file system watcher for hybrid mode (no Artifactory feed configured)");
            return CreateFileSystemWatcher(logger, packageWatcherLogger);
        }
    }

    public static HotReloadConfiguration CreateDefaultConfiguration()
    {
        var config = new HotReloadConfiguration();
        
        // Load from environment variables
        config.LoadFromEnvironment();
        
        // Set defaults if not configured
        if (!config.IsArtifactoryFeedConfigured() && !config.IsFileSystemWatchingConfigured())
        {
            // Default to file system watching
            config.Mode = HotReloadMode.FileSystem;
            config.FileSystemWatching = new FileSystemWatchingOptions();
        }
        else if (config.IsArtifactoryFeedConfigured())
        {
            // Prefer Artifactory feed if configured
            config.Mode = HotReloadMode.ArtifactoryFeed;
        }
        
        return config;
    }
}