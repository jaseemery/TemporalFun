using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace TemporalWorkerApp.Watchers;

public class PackageWatcher : IDisposable
{
    private readonly ILogger<PackageWatcher> _logger;
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly ConcurrentDictionary<string, DateTime> _lastChangeTime = new();
    private readonly Timer _debounceTimer;
    private readonly object _lockObject = new();
    private bool _disposed = false;

    public event Action? PackagesChanged;

    public PackageWatcher(ILogger<PackageWatcher> logger)
    {
        _logger = logger;
        _debounceTimer = new Timer(OnDebounceTimerElapsed, null, Timeout.Infinite, Timeout.Infinite);
        SetupWatchers();
    }

    private void SetupWatchers()
    {
        // Watch NuGet package cache directories
        var nugetPaths = GetNuGetCachePaths();
        
        foreach (var path in nugetPaths.Where(Directory.Exists))
        {
            try
            {
                var watcher = new FileSystemWatcher(path)
                {
                    Filter = "*.dll",
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.CreationTime | NotifyFilters.LastWrite | NotifyFilters.FileName
                };

                watcher.Created += OnFileChanged;
                watcher.Changed += OnFileChanged;
                watcher.Deleted += OnFileChanged;
                watcher.Renamed += OnFileRenamed;
                
                watcher.EnableRaisingEvents = true;
                _watchers.Add(watcher);
                
                _logger.LogInformation("Watching for package changes in: {Path}", path);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to setup watcher for path: {Path}", path);
            }
        }

        // Also watch the application's bin directory for direct assembly drops
        var binPath = AppDomain.CurrentDomain.BaseDirectory;
        if (Directory.Exists(binPath))
        {
            try
            {
                var binWatcher = new FileSystemWatcher(binPath)
                {
                    Filter = "*.dll",
                    IncludeSubdirectories = false,
                    NotifyFilter = NotifyFilters.CreationTime | NotifyFilters.LastWrite | NotifyFilters.FileName
                };

                binWatcher.Created += OnFileChanged;
                binWatcher.Changed += OnFileChanged;
                binWatcher.Deleted += OnFileChanged;
                binWatcher.Renamed += OnFileRenamed;
                
                binWatcher.EnableRaisingEvents = true;
                _watchers.Add(binWatcher);
                
                _logger.LogInformation("Watching for assembly changes in application directory: {Path}", binPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to setup watcher for application directory: {Path}", binPath);
            }
        }
    }

    private static string[] GetNuGetCachePaths()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        
        return new[]
        {
            Path.Combine(userProfile, ".nuget", "packages"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NuGet", "v3-cache"),
            "/root/.nuget/packages", // Docker container path
            "/home/.nuget/packages"   // Alternative container path
        };
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        if (ShouldIgnoreFile(e.FullPath)) return;

        _logger.LogDebug("File change detected: {Path} ({ChangeType})", e.FullPath, e.ChangeType);
        ScheduleChangeNotification(e.FullPath);
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        if (ShouldIgnoreFile(e.FullPath)) return;

        _logger.LogDebug("File renamed: {OldPath} -> {NewPath}", e.OldFullPath, e.FullPath);
        ScheduleChangeNotification(e.FullPath);
    }

    private bool ShouldIgnoreFile(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        
        // Ignore system and framework assemblies
        var ignorePatterns = new[]
        {
            "System.", "Microsoft.", "netstandard", "mscorlib",
            "Temporalio.dll", // Ignore the main Temporal SDK
            ".tmp", ".temp"
        };

        return ignorePatterns.Any(pattern => fileName.StartsWith(pattern, StringComparison.OrdinalIgnoreCase));
    }

    private void ScheduleChangeNotification(string filePath)
    {
        lock (_lockObject)
        {
            _lastChangeTime[filePath] = DateTime.UtcNow;
            
            // Debounce changes to avoid multiple rapid notifications
            _debounceTimer.Change(TimeSpan.FromSeconds(2), Timeout.InfiniteTimeSpan);
        }
    }

    private void OnDebounceTimerElapsed(object? state)
    {
        lock (_lockObject)
        {
            if (_lastChangeTime.Any())
            {
                var changedFiles = _lastChangeTime.Keys.ToList();
                _lastChangeTime.Clear();
                
                _logger.LogInformation("Package changes detected in {FileCount} files, triggering reload", changedFiles.Count);
                
                try
                {
                    PackagesChanged?.Invoke();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred while handling package changes");
                }
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        foreach (var watcher in _watchers)
        {
            try
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing file watcher");
            }
        }
        
        _watchers.Clear();
        
        _debounceTimer?.Dispose();
        _disposed = true;
        
        _logger.LogInformation("Package watcher disposed");
    }
}