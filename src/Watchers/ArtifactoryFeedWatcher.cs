using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace TemporalWorkerApp.Watchers;

public class ArtifactoryFeedWatcher : IDisposable
{
    private readonly ILogger<ArtifactoryFeedWatcher> _logger;
    private readonly HttpClient _httpClient;
    private readonly Timer _pollTimer;
    private readonly ConcurrentDictionary<string, string> _lastKnownVersions = new();
    private readonly string _feedUrl;
    private readonly string _downloadPath;
    private readonly TimeSpan _pollInterval;
    private readonly List<string> _packageFilters;
    private bool _disposed = false;
    private int _consecutiveFailures = 0;
    private DateTime _lastFailureTime = DateTime.MinValue;
    private readonly int _maxConsecutiveFailures = 5;
    private readonly TimeSpan _circuitBreakerTimeout = TimeSpan.FromMinutes(5);

    public event Action<IEnumerable<string>>? NewPackagesDetected;

    public ArtifactoryFeedWatcher(
        ILogger<ArtifactoryFeedWatcher> logger,
        string feedUrl,
        string? username = null,
        string? password = null,
        TimeSpan? pollInterval = null,
        IEnumerable<string>? packageFilters = null)
    {
        _logger = logger;
        _feedUrl = feedUrl.TrimEnd('/');
        _downloadPath = Path.Combine(Path.GetTempPath(), "TemporalWorker", "FeedPackages");
        _pollInterval = pollInterval ?? TimeSpan.FromSeconds(30);
        _packageFilters = packageFilters?.ToList() ?? new List<string>();

        // Create download directory
        Directory.CreateDirectory(_downloadPath);

        // Configure HTTP client
        _httpClient = new HttpClient();
        if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
        {
            var authValue = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authValue);
        }

        // Start polling timer
        _pollTimer = new Timer(async _ => await PollFeedAsync(), null, TimeSpan.Zero, _pollInterval);
        
        _logger.LogInformation("Started monitoring Artifactory feed: {FeedUrl} (Poll interval: {Interval})", 
            _feedUrl, _pollInterval);
    }

    private async Task PollFeedAsync()
    {
        if (_disposed) return;

        // Circuit breaker check
        if (_consecutiveFailures >= _maxConsecutiveFailures)
        {
            if (DateTime.UtcNow - _lastFailureTime < _circuitBreakerTimeout)
            {
                _logger.LogWarning("Circuit breaker is open. Skipping feed poll due to {Failures} consecutive failures", _consecutiveFailures);
                return;
            }
            else
            {
                _logger.LogInformation("Circuit breaker timeout expired. Attempting to resume feed polling");
                _consecutiveFailures = 0;
            }
        }

        try
        {
            _logger.LogDebug("Polling Artifactory feed for package updates...");
            
            var newPackages = new List<string>();
            
            // If we have specific package filters, check each one
            if (_packageFilters.Any())
            {
                foreach (var packageFilter in _packageFilters)
                {
                    var packages = await CheckPackageAsync(packageFilter);
                    newPackages.AddRange(packages);
                }
            }
            else
            {
                // Search for all Temporal-related packages
                var packages = await SearchPackagesAsync("Temporal");
                newPackages.AddRange(packages);
            }

            if (newPackages.Any())
            {
                _logger.LogInformation("Detected {Count} new/updated packages", newPackages.Count);
                NewPackagesDetected?.Invoke(newPackages);
            }
            
            // Reset failure count on successful poll
            _consecutiveFailures = 0;
        }
        catch (Exception ex)
        {
            _consecutiveFailures++;
            _lastFailureTime = DateTime.UtcNow;
            
            if (_consecutiveFailures >= _maxConsecutiveFailures)
            {
                _logger.LogError(ex, "Circuit breaker opened after {Failures} consecutive failures. Feed polling will be suspended for {Timeout}", 
                    _consecutiveFailures, _circuitBreakerTimeout);
            }
            else
            {
                _logger.LogWarning(ex, "Error polling Artifactory feed (failure {Failures}/{MaxFailures})", _consecutiveFailures, _maxConsecutiveFailures);
            }
        }
    }

    private async Task<List<string>> CheckPackageAsync(string packageId)
    {
        try
        {
            // Get package registration data
            var registrationUrl = $"{_feedUrl}/registration/{packageId.ToLowerInvariant()}/index.json";
            var response = await _httpClient.GetAsync(registrationUrl);
            
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode != System.Net.HttpStatusCode.NotFound)
                {
                    _logger.LogWarning("Failed to get registration for package {PackageId}: {StatusCode}", 
                        packageId, response.StatusCode);
                }
                return new List<string>();
            }

            var content = await response.Content.ReadAsStringAsync();
            var registration = JsonSerializer.Deserialize<PackageRegistration>(content);
            
            if (registration?.Items == null || !registration.Items.Any())
            {
                return new List<string>();
            }

            var newPackages = new List<string>();
            var latestVersion = registration.Items
                .SelectMany(item => item.Items ?? new List<PackageVersion>())
                .Where(v => v.CatalogEntry != null)
                .OrderByDescending(v => Version.Parse(v.CatalogEntry.Version))
                .FirstOrDefault();

            if (latestVersion?.CatalogEntry != null)
            {
                var currentVersion = latestVersion.CatalogEntry.Version;
                var lastKnownVersion = _lastKnownVersions.GetValueOrDefault(packageId);

                if (lastKnownVersion != currentVersion)
                {
                    _logger.LogInformation("Package {PackageId} updated from {OldVersion} to {NewVersion}", 
                        packageId, lastKnownVersion ?? "none", currentVersion);

                    // Download the package
                    var packagePath = await DownloadPackageAsync(packageId, currentVersion);
                    if (packagePath != null)
                    {
                        newPackages.Add(packagePath);
                        _lastKnownVersions[packageId] = currentVersion;
                    }
                }
            }

            return newPackages;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking package {PackageId}", packageId);
            return new List<string>();
        }
    }

    private async Task<List<string>> SearchPackagesAsync(string query)
    {
        try
        {
            // Search for packages matching the query
            var searchUrl = $"{_feedUrl}/query?q={Uri.EscapeDataString(query)}&take=20";
            var response = await _httpClient.GetAsync(searchUrl);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to search packages: {StatusCode}", response.StatusCode);
                return new List<string>();
            }

            var content = await response.Content.ReadAsStringAsync();
            var searchResult = JsonSerializer.Deserialize<PackageSearchResult>(content);
            
            if (searchResult?.Data == null)
            {
                return new List<string>();
            }

            var newPackages = new List<string>();
            
            foreach (var package in searchResult.Data)
            {
                if (package.Id == null || package.Version == null) continue;

                var lastKnownVersion = _lastKnownVersions.GetValueOrDefault(package.Id);
                
                if (lastKnownVersion != package.Version)
                {
                    _logger.LogInformation("Found new package {PackageId} version {Version}", 
                        package.Id, package.Version);

                    // Download the package
                    var packagePath = await DownloadPackageAsync(package.Id, package.Version);
                    if (packagePath != null)
                    {
                        newPackages.Add(packagePath);
                        _lastKnownVersions[package.Id] = package.Version;
                    }
                }
            }

            return newPackages;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching packages with query: {Query}", query);
            return new List<string>();
        }
    }

    private async Task<string?> DownloadPackageAsync(string packageId, string version)
    {
        try
        {
            // Download package using the flat container URL
            var downloadUrl = $"{_feedUrl}/flatcontainer/{packageId.ToLowerInvariant()}/{version.ToLowerInvariant()}/{packageId.ToLowerInvariant()}.{version.ToLowerInvariant()}.nupkg";
            
            _logger.LogDebug("Downloading package from: {Url}", downloadUrl);
            
            var response = await _httpClient.GetAsync(downloadUrl);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to download package {PackageId} {Version}: {StatusCode}", 
                    packageId, version, response.StatusCode);
                return null;
            }

            var packageFileName = $"{packageId}.{version}.nupkg";
            var packagePath = Path.Combine(_downloadPath, packageFileName);
            
            // Create versioned directory to avoid conflicts
            var versionedPath = Path.Combine(_downloadPath, packageId, version);
            Directory.CreateDirectory(versionedPath);
            var versionedPackagePath = Path.Combine(versionedPath, packageFileName);

            await using var fileStream = File.Create(versionedPackagePath);
            await response.Content.CopyToAsync(fileStream);

            _logger.LogInformation("Downloaded package {PackageId} {Version} to {Path}", 
                packageId, version, versionedPackagePath);

            return versionedPackagePath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading package {PackageId} {Version}", packageId, version);
            return null;
        }
    }

    public void AddPackageFilter(string packageId)
    {
        if (!_packageFilters.Contains(packageId))
        {
            _packageFilters.Add(packageId);
            _logger.LogInformation("Added package filter: {PackageId}", packageId);
        }
    }

    public void RemovePackageFilter(string packageId)
    {
        if (_packageFilters.Remove(packageId))
        {
            _logger.LogInformation("Removed package filter: {PackageId}", packageId);
        }
    }

    public void ClearOldPackages(TimeSpan olderThan)
    {
        try
        {
            var cutoffTime = DateTime.UtcNow - olderThan;
            var directories = Directory.GetDirectories(_downloadPath);

            foreach (var dir in directories)
            {
                var dirInfo = new DirectoryInfo(dir);
                if (dirInfo.LastWriteTimeUtc < cutoffTime)
                {
                    Directory.Delete(dir, true);
                    _logger.LogDebug("Cleaned up old package directory: {Path}", dir);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error cleaning up old packages");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        _disposed = true;
        _pollTimer?.Dispose();
        _httpClient?.Dispose();
        
        _logger.LogInformation("Artifactory feed watcher disposed");
    }
}

// JSON models for Artifactory NuGet API responses
public class PackageSearchResult
{
    public List<PackageSearchItem>? Data { get; set; }
}

public class PackageSearchItem
{
    public string? Id { get; set; }
    public string? Version { get; set; }
    public string? Description { get; set; }
    public List<string>? Authors { get; set; }
    public string? IconUrl { get; set; }
    public string? LicenseUrl { get; set; }
    public string? ProjectUrl { get; set; }
    public List<string>? Tags { get; set; }
    public string? Title { get; set; }
    public long? TotalDownloads { get; set; }
    public bool? Verified { get; set; }
}

public class PackageRegistration
{
    public int? Count { get; set; }
    public List<RegistrationPage>? Items { get; set; }
}

public class RegistrationPage
{
    public int? Count { get; set; }
    public List<PackageVersion>? Items { get; set; }
    public string? Lower { get; set; }
    public string? Upper { get; set; }
}

public class PackageVersion
{
    public string? Id { get; set; }
    public CatalogEntry? CatalogEntry { get; set; }
    public string? PackageContent { get; set; }
    public string? Registration { get; set; }
}

public class CatalogEntry
{
    public string? Id { get; set; }
    public string? Version { get; set; }
    public List<string>? Authors { get; set; }
    public List<DependencyGroup>? DependencyGroups { get; set; }
    public string? Description { get; set; }
    public string? IconUrl { get; set; }
    public string? Language { get; set; }
    public string? LicenseUrl { get; set; }
    public bool? Listed { get; set; }
    public string? MinClientVersion { get; set; }
    public string? PackageContent { get; set; }
    public string? ProjectUrl { get; set; }
    public DateTime? Published { get; set; }
    public bool? RequireLicenseAcceptance { get; set; }
    public string? Summary { get; set; }
    public List<string>? Tags { get; set; }
    public string? Title { get; set; }
}

public class DependencyGroup
{
    public string? TargetFramework { get; set; }
    public List<Dependency>? Dependencies { get; set; }
}

public class Dependency
{
    public string? Id { get; set; }
    public string? Range { get; set; }
    public string? Registration { get; set; }
}