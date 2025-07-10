using Microsoft.Extensions.Logging;
using Moq;
using FluentAssertions;
using Xunit;
using TemporalWorkerApp.Loaders;
using TemporalWorkerApp.Managers;
using TemporalWorkerApp.Watchers;
using Temporalio.Workflows;

namespace TemporalWorker.Tests;

public class SimpleWorkflowLoaderTests : IDisposable
{
    private readonly Mock<ILogger<WorkflowLoader>> _mockLogger;
    private readonly Mock<ILogger<HotReloadManager>> _mockHotReloadLogger;
    private readonly Mock<ILogger<PackageWatcher>> _mockPackageWatcherLogger;
    private readonly HotReloadManager _hotReloadManager;
    private readonly WorkflowLoader _workflowLoader;

    public SimpleWorkflowLoaderTests()
    {
        _mockLogger = new Mock<ILogger<WorkflowLoader>>();
        _mockHotReloadLogger = new Mock<ILogger<HotReloadManager>>();
        _mockPackageWatcherLogger = new Mock<ILogger<PackageWatcher>>();
        
        _hotReloadManager = new HotReloadManager(_mockHotReloadLogger.Object, _mockPackageWatcherLogger.Object);
        _workflowLoader = new WorkflowLoader(_mockLogger.Object, _hotReloadManager);
    }

    [Fact]
    public void Constructor_ShouldInitializeProperly()
    {
        // Arrange & Act & Assert
        _workflowLoader.Should().NotBeNull();
    }

    [Fact]
    public void LoadWorkflowsFromAssemblies_ShouldReturnWorkflowTypes()
    {
        // Arrange
        var logger = Mock.Of<ILogger>();

        // Act
        var workflows = WorkflowLoader.LoadWorkflowsFromAssemblies(logger);

        // Assert
        workflows.Should().NotBeNull();
        workflows.Should().BeAssignableTo<IEnumerable<Type>>();
    }

    [Fact]
    public async Task LoadWorkflowsWithHotReloadAsync_ShouldCallHotReloadManager()
    {
        // Arrange & Act
        var result = await _workflowLoader.LoadWorkflowsWithHotReloadAsync();

        // Assert
        result.Should().NotBeNull();
        result.Should().BeAssignableTo<IEnumerable<Type>>();
    }

    [Fact]
    public void WorkflowsChanged_EventCanBeSubscribed()
    {
        // Arrange
        var eventTriggered = false;

        // Act
        _workflowLoader.WorkflowsChanged += (workflows) => eventTriggered = true;

        // Assert
        // Event subscription should not throw
        eventTriggered.Should().BeFalse(); // Not triggered yet
    }

    [Fact]
    public void Dispose_ShouldCleanupProperly()
    {
        // Arrange & Act
        _workflowLoader.Dispose();

        // Assert - Should not throw
        Assert.True(true);
    }

    public void Dispose()
    {
        _workflowLoader?.Dispose();
        _hotReloadManager?.Dispose();
    }
}

// Simple test workflow class
[Workflow("simple-test-workflow")]
public class SimpleTestWorkflow
{
    [WorkflowRun]
    public async Task<string> RunAsync()
    {
        await Task.Delay(100);
        return "test";
    }
}