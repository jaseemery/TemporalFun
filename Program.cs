using Temporalio.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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
            })
            .Build();

        var logger = host.Services.GetRequiredService<ILogger<Program>>();
        
        // Get configuration
        var temporalServer = Environment.GetEnvironmentVariable("TEMPORAL_SERVER") ?? "localhost:7233";
        var taskQueue = Environment.GetEnvironmentVariable("TASK_QUEUE") ?? "default";
        var environment = Environment.GetEnvironmentVariable("ENVIRONMENT") ?? "unknown";
        var workerIdentity = Environment.GetEnvironmentVariable("WORKER_IDENTITY") ?? "worker-1";
        
        logger.LogInformation("Starting Temporal Worker");
        logger.LogInformation("Environment: {Environment}", environment);
        logger.LogInformation("Worker Identity: {WorkerIdentity}", workerIdentity);
        logger.LogInformation("Task Queue: {TaskQueue}", taskQueue);
        logger.LogInformation("Temporal Server: {Server}", temporalServer);

        // Connect to Temporal with retry logic
        var client = await ConnectToTemporalWithRetryAsync(temporalServer, logger);
        logger.LogInformation("Connected to Temporal server successfully");

        // Load activities from local assemblies
        var activities = new Delegate[]
        {
            TemporalWorkerApp.Activities.EmailActivity.SendEmail,
            TemporalWorkerApp.Activities.DatabaseActivity.SaveData,
            TemporalWorkerApp.Activities.DatabaseActivity.GetData
        };

        // Load workflows from local assemblies
        var workflows = new Type[]
        {
            typeof(TemporalWorkerApp.Workflows.SimpleWorkflow),
            typeof(TemporalWorkerApp.Workflows.CustomerOnboardingWorkflow)
        };

        var workerOptions = new Temporalio.Worker.TemporalWorkerOptions(taskQueue);
        
        foreach (var activity in activities)
        {
            workerOptions.AddActivity(activity);
        }
        
        foreach (var workflow in workflows)
        {
            workerOptions.AddWorkflow(workflow);
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

        logger.LogInformation("Starting Temporal worker with {ActivityCount} activities and {WorkflowCount} workflows on task queue: {TaskQueue}", 
            activities.Length, workflows.Length, taskQueue);
        
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