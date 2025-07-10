using System.Reflection;
using Temporalio.Workflows;
using Microsoft.Extensions.Logging;
using TemporalWorkerApp.Managers;

namespace TemporalWorkerApp.Loaders;

public class WorkflowLoader : IDisposable
{
    private readonly ILogger<WorkflowLoader> _logger;
    private readonly HotReloadManager _hotReloadManager;
    private bool _disposed = false;

    public event Action<IEnumerable<Type>>? WorkflowsChanged;

    public WorkflowLoader(ILogger<WorkflowLoader> logger, HotReloadManager hotReloadManager)
    {
        _logger = logger;
        _hotReloadManager = hotReloadManager;
        _hotReloadManager.WorkflowsReloaded += OnWorkflowsReloaded;
    }

    private void OnWorkflowsReloaded(IEnumerable<Type> newWorkflows)
    {
        _logger.LogInformation("Workflows reloaded via hot reload, notifying worker...");
        WorkflowsChanged?.Invoke(newWorkflows);
    }

    public async Task<IEnumerable<Type>> LoadWorkflowsWithHotReloadAsync()
    {
        _logger.LogInformation("Loading workflows with hot reload support...");
        return await _hotReloadManager.ReloadWorkflowsAsync();
    }

    public static IEnumerable<Type> LoadWorkflowsFromAssemblies(ILogger logger)
    {
        var workflows = new List<Type>();

        try
        {
            // Get all loaded assemblies
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();

            foreach (var assembly in assemblies)
            {
                try
                {
                    // Skip system assemblies and assemblies we can't inspect
                    if (assembly.IsDynamic || 
                        assembly.FullName?.StartsWith("System") == true ||
                        assembly.FullName?.StartsWith("Microsoft") == true ||
                        assembly.FullName?.StartsWith("Temporalio") == true)
                    {
                        continue;
                    }

                    var assemblyWorkflows = ScanAssemblyForWorkflows(assembly, logger);
                    workflows.AddRange(assemblyWorkflows);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to scan assembly {Assembly} for workflows", assembly.FullName ?? "Unknown");
                }
            }

            logger.LogInformation("Found {Count} workflows from loaded assemblies", workflows.Count);
            
            // If no workflows found from packages, add local workflows as fallback
            if (!workflows.Any())
            {
                logger.LogWarning("No workflows found from NuGet packages, using local workflows");
                var localWorkflows = GetLocalWorkflows();
                workflows.AddRange(localWorkflows);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error loading workflows from assemblies");
        }

        return workflows;
    }

    private static IEnumerable<Type> ScanAssemblyForWorkflows(Assembly assembly, ILogger logger)
    {
        var workflows = new List<Type>();

        try
        {
            var types = assembly.GetTypes();
            
            foreach (var type in types)
            {
                try
                {
                    // Check if the type is a workflow (has WorkflowAttribute or implements IWorkflow)
                    if (IsWorkflowType(type))
                    {
                        workflows.Add(type);
                        logger.LogDebug("Found workflow: {WorkflowName} from {Assembly}", 
                            type.Name, assembly.GetName().Name);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Error checking type {Type} for workflow attributes", type.FullName);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to get types from assembly {Assembly}", assembly.FullName);
        }

        return workflows;
    }

    private static bool IsWorkflowType(Type type)
    {
        // Check if type has WorkflowAttribute
        if (type.GetCustomAttribute<WorkflowAttribute>() != null)
        {
            return true;
        }

        // Check if type implements any workflow interface
        var interfaces = type.GetInterfaces();
        if (interfaces.Any(i => i.Name.Contains("Workflow") || i.Namespace?.Contains("Temporalio.Workflows") == true))
        {
            return true;
        }

        // Check if class name suggests it's a workflow
        if (type.Name.EndsWith("Workflow") && type.IsClass && !type.IsAbstract)
        {
            return true;
        }

        return false;
    }

    private static IEnumerable<Type> GetLocalWorkflows()
    {
        // Return sample local workflows if no external workflows are found
        return new List<Type>
        {
            // Add your local workflow types here
            // Example: typeof(MyLocalWorkflow)
        };
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _hotReloadManager.WorkflowsReloaded -= OnWorkflowsReloaded;
            _disposed = true;
        }
    }
}