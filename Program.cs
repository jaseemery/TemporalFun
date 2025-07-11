using Temporalio.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TemporalWorkerApp.Loaders;
using TemporalWorkerApp.Services;
using TemporalWorkerApp.Managers;
using TemporalWorkerApp.Watchers;
using TemporalWorkerApp.Configuration;

namespace TemporalWorkerApp;

class Program
{
    static async Task Main(string[] args)
    {
        var host = Host.CreateDefaultBuilder(args)
            .ConfigureServices((context, services) =>
            {
                services.AddLogging(builder => 
                {
                    builder.AddConsole();
                    builder.SetMinimumLevel(LogLevel.Information);
                });
                
                // Register services for dependency injection
                services.AddSingleton<ActivityLoader>();
                services.AddSingleton<HotReloadWorkerService>();
                services.AddSingleton<TemporalWorkerApp.Services.HealthCheckService>();
            })
            .Build();

        var logger = host.Services.GetRequiredService<ILogger<Program>>();
        
        // Get configuration
        var temporalServer = Environment.GetEnvironmentVariable("TEMPORAL_SERVER") ?? "localhost:7233";
        var taskQueue = Environment.GetEnvironmentVariable("TASK_QUEUE") ?? "default";
        var hotReloadEnabled = Environment.GetEnvironmentVariable("HOT_RELOAD_ENABLED")?.ToLowerInvariant() != "false";
        
        logger.LogInformation("Starting Temporal Worker with hot reload: {HotReloadEnabled}", hotReloadEnabled);
        logger.LogInformation("Connecting to Temporal server: {Server}", temporalServer);
        
        // Connect to Temporal with retry logic
        var client = await ConnectToTemporalWithRetryAsync(temporalServer, logger);

        logger.LogInformation("Connected to Temporal server successfully");

        if (hotReloadEnabled)
        {
            // Use hot reload service
            logger.LogInformation("Starting worker with hot reload capability...");
            
            var activityLoader = new ActivityLoader(
                host.Services.GetRequiredService<ILogger<ActivityLoader>>(),
                host.Services.GetRequiredService<ILogger<TemporalWorkerApp.Managers.HotReloadManager>>(),
                host.Services.GetRequiredService<ILogger<TemporalWorkerApp.Watchers.PackageWatcher>>()
            );

            var workflowLoader = new WorkflowLoader(
                host.Services.GetRequiredService<ILogger<WorkflowLoader>>(),
                activityLoader.HotReloadManager
            );
            
            var workerService = new HotReloadWorkerService(
                host.Services.GetRequiredService<ILogger<TemporalWorkerApp.Services.HotReloadWorkerService>>(),
                client,
                taskQueue,
                activityLoader,
                workflowLoader
            );

            // Start health check service
            var healthCheckService = new TemporalWorkerApp.Services.HealthCheckService(
                host.Services.GetRequiredService<ILogger<TemporalWorkerApp.Services.HealthCheckService>>(),
                workerService
            );
            _ = healthCheckService.StartAsync(CancellationToken.None);

            // Run the hot reload service with graceful shutdown
            using var cts = new CancellationTokenSource();
            var shutdownRequested = false;
            
            Console.CancelKeyPress += (_, e) =>
            {
                if (!shutdownRequested)
                {
                    shutdownRequested = true;
                    e.Cancel = true;
                    logger.LogInformation("Graceful shutdown requested. Press Ctrl+C again to force exit.");
                    
                    Task.Run(() =>
                    {
                        try
                        {
                            logger.LogInformation("Initiating graceful shutdown...");
                            cts.Cancel();
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(ex, "Error during graceful shutdown");
                        }
                    });
                }
                else
                {
                    logger.LogWarning("Force exit requested");
                    Environment.Exit(1);
                }
            };

            try
            {
                await workerService.StartAsync(cts.Token);
                await Task.Delay(Timeout.Infinite, cts.Token);
            }
            catch (OperationCanceledException)
            {
                logger.LogInformation("Application is shutting down gracefully...");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error during application execution");
                throw;
            }
            finally
            {
                logger.LogInformation("Performing cleanup...");
                try
                {
                    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    await workerService.StopAsync(timeoutCts.Token);
                    await healthCheckService.StopAsync(timeoutCts.Token);
                    activityLoader.Dispose();
                    logger.LogInformation("Cleanup completed successfully");
                }
                catch (OperationCanceledException)
                {
                    logger.LogWarning("Cleanup timed out after 30 seconds");
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error during cleanup");
                }
            }
        }
        else
        {
            // Traditional mode without hot reload
            logger.LogInformation("Starting worker in traditional mode (no hot reload)...");
            
            // Start health check service for traditional mode too
            var healthCheckService = new TemporalWorkerApp.Services.HealthCheckService(
                host.Services.GetRequiredService<ILogger<TemporalWorkerApp.Services.HealthCheckService>>(),
                null // No hot reload worker service in traditional mode
            );
            _ = healthCheckService.StartAsync(CancellationToken.None);
            
            var activities = ActivityLoader.LoadActivitiesFromAssemblies(logger);
            var activityList = activities.ToList();
            
            if (!activityList.Any())
            {
                logger.LogWarning("No activities found from NuGet packages, using local activities");
                activityList.AddRange(new Delegate[]
                {
                    TemporalWorkerApp.Activities.EmailActivity.SendEmail,
                    TemporalWorkerApp.Activities.DatabaseActivity.SaveData,
                    TemporalWorkerApp.Activities.DatabaseActivity.GetData
                });
            }

            var workerOptions = new Temporalio.Worker.TemporalWorkerOptions(taskQueue);
            
            foreach (var activity in activityList)
            {
                workerOptions.AddActivity(activity);
            }
            
            using var worker = new Temporalio.Worker.TemporalWorker(client, workerOptions);
            using var cts = new CancellationTokenSource();
            var shutdownRequested = false;

            Console.CancelKeyPress += (_, e) =>
            {
                if (!shutdownRequested)
                {
                    shutdownRequested = true;
                    e.Cancel = true;
                    logger.LogInformation("Graceful shutdown requested. Press Ctrl+C again to force exit.");
                    cts.Cancel();
                }
                else
                {
                    logger.LogWarning("Force exit requested");
                    Environment.Exit(1);
                }
            };

            logger.LogInformation("Starting Temporal worker on task queue: {TaskQueue} with {ActivityCount} activities", 
                taskQueue, activityList.Count);
            
            try
            {
                await worker.ExecuteAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                logger.LogInformation("Worker execution cancelled gracefully");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in worker execution");
                throw;
            }
            finally
            {
                logger.LogInformation("Worker shutdown completed");
                
                // Cleanup health check service in traditional mode
                try
                {
                    await healthCheckService.StopAsync(CancellationToken.None);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error stopping health check service");
                }
            }
        }
    }

    private static async Task<TemporalClient> ConnectToTemporalWithRetryAsync(string temporalServer, ILogger logger)
    {
        const int maxRetries = 5;
        var baseDelay = TimeSpan.FromSeconds(2);
        
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                logger.LogInformation("Attempting to connect to Temporal server (attempt {Attempt}/{MaxRetries})", attempt, maxRetries);
                
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                
                var client = await TemporalClient.ConnectAsync(new()
                {
                    TargetHost = temporalServer,
                });
                
                // Test connection by checking system info
                await client.Connection.WorkflowService.GetSystemInfoAsync(new());
                
                logger.LogInformation("Successfully connected to Temporal server on attempt {Attempt}", attempt);
                return client;
            }
            catch (Exception ex) when (attempt < maxRetries)
            {
                var delay = TimeSpan.FromMilliseconds(baseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1));
                logger.LogWarning(ex, "Failed to connect to Temporal server on attempt {Attempt}. Retrying in {Delay}ms", attempt, delay.TotalMilliseconds);
                
                await Task.Delay(delay);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to connect to Temporal server after {MaxRetries} attempts", maxRetries);
                throw new InvalidOperationException($"Unable to connect to Temporal server at {temporalServer} after {maxRetries} attempts", ex);
            }
        }
        
        throw new InvalidOperationException($"Unable to connect to Temporal server at {temporalServer}");
    }
}