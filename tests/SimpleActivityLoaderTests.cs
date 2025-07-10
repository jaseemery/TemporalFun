using Microsoft.Extensions.Logging;
using Moq;
using FluentAssertions;
using Xunit;
using TemporalWorkerApp.Loaders;
using TemporalWorkerApp.Managers;
using TemporalWorkerApp.Watchers;
using Temporalio.Activities;

namespace TemporalWorker.Tests;

public class SimpleActivityLoaderTests : IDisposable
{
    private readonly Mock<ILogger<ActivityLoader>> _mockLogger;
    private readonly Mock<ILogger<HotReloadManager>> _mockHotReloadLogger;
    private readonly Mock<ILogger<PackageWatcher>> _mockPackageWatcherLogger;
    private readonly ActivityLoader _activityLoader;

    public SimpleActivityLoaderTests()
    {
        _mockLogger = new Mock<ILogger<ActivityLoader>>();
        _mockHotReloadLogger = new Mock<ILogger<HotReloadManager>>();
        _mockPackageWatcherLogger = new Mock<ILogger<PackageWatcher>>();
        
        _activityLoader = new ActivityLoader(
            _mockLogger.Object,
            _mockHotReloadLogger.Object,
            _mockPackageWatcherLogger.Object
        );
    }

    [Fact]
    public void Constructor_ShouldInitializeProperly()
    {
        // Arrange & Act & Assert
        _activityLoader.Should().NotBeNull();
        _activityLoader.HotReloadManager.Should().NotBeNull();
    }

    [Fact]
    public void HotReloadManager_ShouldBeExposed()
    {
        // Arrange & Act
        var hotReloadManager = _activityLoader.HotReloadManager;

        // Assert
        hotReloadManager.Should().NotBeNull();
        hotReloadManager.Should().BeOfType<HotReloadManager>();
    }

    [Fact]
    public async Task LoadActivitiesWithHotReloadAsync_ShouldReturnDelegates()
    {
        // Arrange & Act
        var activities = await _activityLoader.LoadActivitiesWithHotReloadAsync();

        // Assert
        activities.Should().NotBeNull();
        activities.Should().BeAssignableTo<IEnumerable<Delegate>>();
    }

    [Fact]
    public void LoadActivitiesFromAssemblies_ShouldReturnActivities()
    {
        // Arrange
        var logger = Mock.Of<ILogger>();

        // Act
        var activities = ActivityLoader.LoadActivitiesFromAssemblies(logger);

        // Assert
        activities.Should().NotBeNull();
        activities.Should().BeAssignableTo<IEnumerable<Delegate>>();
    }

    [Fact]
    public void ActivitiesChanged_EventCanBeSubscribed()
    {
        // Arrange
        var eventTriggered = false;

        // Act & Assert
        _activityLoader.ActivitiesChanged += (activities) => eventTriggered = true;
        
        // Event subscription should not throw
        eventTriggered.Should().BeFalse(); // Not triggered yet
    }

    [Fact]
    public void Dispose_ShouldCleanupProperly()
    {
        // Arrange & Act
        _activityLoader.Dispose();

        // Assert - Should not throw
        var act = () => _activityLoader.Dispose();
        act.Should().NotThrow("Multiple dispose calls should be safe");
    }

    public void Dispose()
    {
        _activityLoader?.Dispose();
    }
}

// Simple test activity methods
public static class SimpleTestActivityMethods
{
    [Activity]
    public static void SimpleVoidActivity()
    {
        // Simple test activity
    }

    [Activity]
    public static string SimpleStringActivity(string input)
    {
        return $"Processed: {input}";
    }

    [Activity]
    public static async Task<string> SimpleAsyncActivity(string input)
    {
        await Task.Delay(1);
        return $"Async processed: {input}";
    }
}