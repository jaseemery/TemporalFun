using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;

namespace TemporalWorkerApp.Services;

public class HealthCheckService : BackgroundService
{
    private readonly ILogger<HealthCheckService> _logger;
    private readonly HttpListener _listener;
    private readonly HotReloadWorkerService _workerService;
    private volatile bool _isHealthy = true;

    public HealthCheckService(ILogger<HealthCheckService> logger, HotReloadWorkerService workerService)
    {
        _logger = logger;
        _workerService = workerService;
        _listener = new HttpListener();
        _listener.Prefixes.Add("http://localhost:8085/health/");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _listener.Start();
            _logger.LogInformation("Health check service started on http://localhost:8085/health/");

            while (!stoppingToken.IsCancellationRequested)
            {
                var context = await _listener.GetContextAsync();
                _ = Task.Run(() => HandleRequest(context), stoppingToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check service error");
        }
        finally
        {
            _listener?.Stop();
        }
    }

    private async Task HandleRequest(HttpListenerContext context)
    {
        try
        {
            var healthStatus = new
            {
                status = _isHealthy ? "healthy" : "unhealthy",
                timestamp = DateTime.UtcNow,
                version = GetType().Assembly.GetName().Version?.ToString(),
                uptime = DateTime.UtcNow - Process.GetCurrentProcess().StartTime,
                memory = new
                {
                    workingSet = GC.GetTotalMemory(false),
                    gen0Collections = GC.CollectionCount(0),
                    gen1Collections = GC.CollectionCount(1),
                    gen2Collections = GC.CollectionCount(2)
                },
                worker = new
                {
                    isRunning = _workerService != null,
                    restartCount = 0 // Would need to track this
                }
            };

            var json = JsonSerializer.Serialize(healthStatus, new JsonSerializerOptions { WriteIndented = true });
            var buffer = Encoding.UTF8.GetBytes(json);

            context.Response.ContentType = "application/json";
            context.Response.StatusCode = _isHealthy ? 200 : 503;
            context.Response.ContentLength64 = buffer.Length;

            await context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            context.Response.OutputStream.Close();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling health check request");
        }
    }

    public void SetHealthy(bool isHealthy)
    {
        _isHealthy = isHealthy;
        _logger.LogInformation("Health status changed to: {Status}", isHealthy ? "healthy" : "unhealthy");
    }

    public override void Dispose()
    {
        _listener?.Stop();
        _listener?.Close();
        base.Dispose();
    }
}