using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Temporalio.Client;
using Temporalio.Worker;
using TemporalWorkerApp.Loaders;

namespace TemporalWorkerApp.Services;

public class HotReloadWorkerService : BackgroundService
{
    private readonly ILogger<HotReloadWorkerService> _logger;
    private readonly TemporalClient _client;
    private readonly string _taskQueue;
    private readonly ActivityLoader _activityLoader;
    private readonly WorkflowLoader _workflowLoader;
    
    private Temporalio.Worker.TemporalWorker? _currentWorker;
    private readonly object _workerLock = new();
    private CancellationTokenSource? _currentWorkerCts;

    public HotReloadWorkerService(
        ILogger<HotReloadWorkerService> logger,
        TemporalClient client,
        string taskQueue,
        ActivityLoader activityLoader,
        WorkflowLoader workflowLoader)
    {
        _logger = logger;
        _client = client;
        _taskQueue = taskQueue;
        _activityLoader = activityLoader;
        _workflowLoader = workflowLoader;
        
        _activityLoader.ActivitiesChanged += OnActivitiesChanged;
        _workflowLoader.WorkflowsChanged += OnWorkflowsChanged;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting Hot Reload Worker Service...");
        
        try
        {
            // Load initial activities and start the worker
            await StartWorkerWithActivitiesAsync(stoppingToken);
            
            // Keep the service running until cancellation is requested
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Hot Reload Worker Service is shutting down");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in Hot Reload Worker Service");
            throw;
        }
        finally
        {
            await StopCurrentWorkerAsync();
        }
    }

    private async Task StartWorkerWithActivitiesAsync(CancellationToken cancellationToken)
    {
        var activities = await _activityLoader.LoadActivitiesWithHotReloadAsync();
        var workflows = await _workflowLoader.LoadWorkflowsWithHotReloadAsync();
        await StartWorkerAsync(activities, workflows, cancellationToken);
    }

    private void OnActivitiesChanged(IEnumerable<Delegate> newActivities)
    {
        _logger.LogInformation("Activities changed detected, restarting worker...");
        
        Task.Run(async () =>
        {
            try
            {
                await RestartWorkerWithNewComponentsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error restarting worker with new activities");
            }
        });
    }

    private void OnWorkflowsChanged(IEnumerable<Type> newWorkflows)
    {
        _logger.LogInformation("Workflows changed detected, restarting worker...");
        
        Task.Run(async () =>
        {
            try
            {
                await RestartWorkerWithNewComponentsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error restarting worker with new workflows");
            }
        });
    }

    private async Task RestartWorkerWithNewComponentsAsync()
    {
        _logger.LogInformation("Initiating graceful worker restart for hot reload...");
        
        CancellationTokenSource? ctsToCancel = null;
        TemporalWorker? workerToDispose = null;
        
        lock (_workerLock)
        {
            _logger.LogInformation("Stopping current worker gracefully...");
            
            // Capture references to avoid race conditions
            ctsToCancel = _currentWorkerCts;
            workerToDispose = _currentWorker;
            
            // Clear current references immediately
            _currentWorker = null;
            _currentWorkerCts = null;
        }

        // Cancel outside of lock to avoid race conditions
        try
        {
            ctsToCancel?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            _logger.LogDebug("CancellationTokenSource was already disposed");
        }

        // Wait for graceful shutdown with timeout
        var shutdownTimeout = TimeSpan.FromSeconds(10);
        var shutdownStartTime = DateTime.UtcNow;
        
        while (workerToDispose != null && DateTime.UtcNow - shutdownStartTime < shutdownTimeout)
        {
            await Task.Delay(100);
            
            // Check if worker task has completed
            lock (_workerLock)
            {
                if (_currentWorker == null && workerToDispose != null)
                {
                    // Worker has completed execution
                    break;
                }
            }
        }

        // Clean up resources
        if (workerToDispose != null)
        {
            try
            {
                workerToDispose.Dispose();
                _logger.LogInformation("Previous worker disposed successfully");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing previous worker");
            }
        }

        try
        {
            ctsToCancel?.Dispose();
        }
        catch (ObjectDisposedException)
        {
            // Already disposed, ignore
        }

        // Brief delay to ensure complete cleanup
        await Task.Delay(500);

        // Load fresh activities and workflows
        var activities = await _activityLoader.LoadActivitiesWithHotReloadAsync();
        var workflows = await _workflowLoader.LoadWorkflowsWithHotReloadAsync();

        // Start new worker with updated components
        await StartWorkerAsync(activities, workflows, CancellationToken.None);
        
        _logger.LogInformation("Worker restarted successfully with {ActivityCount} activities and {WorkflowCount} workflows", 
            activities.Count(), workflows.Count());
    }

    private async Task StartWorkerAsync(IEnumerable<Delegate> activities, IEnumerable<Type> workflows, CancellationToken cancellationToken)
    {
        lock (_workerLock)
        {
            var activitiesList = activities.ToList();
            var workflowsList = workflows.ToList();
            
            if (!activitiesList.Any())
            {
                // Fallback to local activities if no external activities found
                _logger.LogWarning("No activities found from packages, using local activities");
                activitiesList.AddRange(new Delegate[]
                {
                    TemporalWorkerApp.Activities.EmailActivity.SendEmail,
                    TemporalWorkerApp.Activities.DatabaseActivity.SaveData,
                    TemporalWorkerApp.Activities.DatabaseActivity.GetData
                });
            }

            var workerOptions = new TemporalWorkerOptions(_taskQueue);
            
            // Add activities
            foreach (var activity in activitiesList)
            {
                workerOptions.AddActivity(activity);
            }

            // Add workflows
            foreach (var workflow in workflowsList)
            {
                workerOptions.AddWorkflow(workflow);
            }

            _currentWorker = new Temporalio.Worker.TemporalWorker(_client, workerOptions);
            _currentWorkerCts = new CancellationTokenSource();
            
            _logger.LogInformation("Starting Temporal worker with {ActivityCount} activities and {WorkflowCount} workflows on task queue: {TaskQueue}", 
                activitiesList.Count, workflowsList.Count, _taskQueue);
        }

        // Start the worker in the background
        _ = Task.Run(async () =>
        {
            TemporalWorker? workerToExecute = null;
            CancellationTokenSource? workerCts = null;
            
            // Capture references safely
            lock (_workerLock)
            {
                workerToExecute = _currentWorker;
                workerCts = _currentWorkerCts;
            }
            
            if (workerToExecute == null || workerCts == null)
            {
                _logger.LogWarning("Worker or CancellationTokenSource is null, cannot start execution");
                return;
            }
            
            try
            {
                using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken, 
                    workerCts.Token);
                    
                await workerToExecute.ExecuteAsync(combinedCts.Token);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Worker execution cancelled gracefully");
            }
            catch (ObjectDisposedException)
            {
                _logger.LogDebug("Worker was disposed during execution");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in worker execution");
            }
            finally
            {
                lock (_workerLock)
                {
                    // Only clear if this is still the current worker
                    if (_currentWorker == workerToExecute)
                    {
                        _logger.LogDebug("Worker execution completed, clearing current worker reference");
                        _currentWorker = null;
                    }
                }
            }
        });

        // Give the worker a moment to start
        await Task.Delay(500, cancellationToken);
    }

    private async Task StopCurrentWorkerAsync()
    {
        _logger.LogInformation("Initiating graceful worker shutdown...");
        
        CancellationTokenSource? ctsToCancel = null;
        TemporalWorker? workerToDispose = null;
        
        lock (_workerLock)
        {
            // Capture references to avoid race conditions
            ctsToCancel = _currentWorkerCts;
            workerToDispose = _currentWorker;
            
            // Clear current references immediately
            _currentWorker = null;
            _currentWorkerCts = null;
        }

        // Cancel outside of lock to avoid race conditions
        try
        {
            ctsToCancel?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            _logger.LogDebug("CancellationTokenSource was already disposed");
        }

        // Wait for graceful shutdown with timeout
        var shutdownTimeout = TimeSpan.FromSeconds(15);
        var shutdownStartTime = DateTime.UtcNow;
        
        while (workerToDispose != null && DateTime.UtcNow - shutdownStartTime < shutdownTimeout)
        {
            await Task.Delay(100);
        }

        // Clean up resources
        if (workerToDispose != null)
        {
            try
            {
                workerToDispose.Dispose();
                _logger.LogInformation("Worker disposed successfully");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing worker");
            }
        }

        try
        {
            ctsToCancel?.Dispose();
        }
        catch (ObjectDisposedException)
        {
            // Already disposed, ignore
        }

        _logger.LogInformation("Current worker stopped gracefully");
        await Task.Delay(100); // Brief delay for cleanup
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping Hot Reload Worker Service...");
        
        await StopCurrentWorkerAsync();
        _activityLoader.Dispose();
        _workflowLoader.Dispose();
        
        await base.StopAsync(cancellationToken);
        
        _logger.LogInformation("Hot Reload Worker Service stopped");
    }
}