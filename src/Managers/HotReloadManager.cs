using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Extensions.Logging;
using Temporalio.Activities;
using Temporalio.Workflows;
using TemporalWorkerApp.Watchers;

namespace TemporalWorkerApp.Managers;

public class HotReloadManager : IDisposable
{
    private readonly ILogger<HotReloadManager> _logger;
    private readonly PackageWatcher _packageWatcher;
    private readonly ConcurrentDictionary<string, WeakReference> _loadedAssemblies = new();
    private readonly ConcurrentDictionary<string, AssemblyLoadContext> _loadContexts = new();
    private readonly object _reloadLock = new();
    private bool _disposed = false;

    public event Action<IEnumerable<Delegate>>? ActivitiesReloaded;
    public event Action<IEnumerable<Type>>? WorkflowsReloaded;

    public HotReloadManager(ILogger<HotReloadManager> logger, ILogger<PackageWatcher> packageWatcherLogger)
    {
        _logger = logger;
        _packageWatcher = new PackageWatcher(packageWatcherLogger);
        _packageWatcher.PackagesChanged += OnPackagesChanged;
        
        _logger.LogInformation("Hot reload manager initialized");
    }

    private void OnPackagesChanged()
    {
        _logger.LogInformation("Package changes detected, initiating hot reload...");
        
        Task.Run(async () =>
        {
            try
            {
                // Small delay to ensure files are fully written
                await Task.Delay(1000);
                
                var newActivities = await ReloadActivitiesAsync();
                var newWorkflows = await ReloadWorkflowsAsync();
                
                ActivitiesReloaded?.Invoke(newActivities);
                WorkflowsReloaded?.Invoke(newWorkflows);
                
                _logger.LogInformation("Hot reload completed successfully with {ActivityCount} activities and {WorkflowCount} workflows", 
                    newActivities.Count(), newWorkflows.Count());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during hot reload process");
            }
        });
    }

    public async Task<IEnumerable<Delegate>> ReloadActivitiesAsync()
    {
        lock (_reloadLock)
        {
            _logger.LogInformation("Starting activity reload process...");
            
            // Unload previous contexts (best effort)
            UnloadPreviousContexts();
            
            // Trigger garbage collection to help with assembly unloading
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            
            return LoadActivitiesFromNewContext();
        }
    }

    public async Task<IEnumerable<Type>> ReloadWorkflowsAsync()
    {
        lock (_reloadLock)
        {
            _logger.LogInformation("Starting workflow reload process...");
            
            // Use the same loaded assemblies that were loaded for activities
            return LoadWorkflowsFromLoadedAssemblies();
        }
    }

    private void UnloadPreviousContexts()
    {
        var contextsToUnload = _loadContexts.Values.ToList();
        _loadContexts.Clear();
        _loadedAssemblies.Clear();
        
        foreach (var context in contextsToUnload)
        {
            try
            {
                if (context.IsCollectible)
                {
                    context.Unload();
                    _logger.LogDebug("Unloaded assembly load context: {Name}", context.Name);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to unload assembly context: {Name}", context.Name);
            }
        }
    }

    private IEnumerable<Delegate> LoadActivitiesFromNewContext()
    {
        var activities = new List<Delegate>();
        
        // Only scan for new assemblies in package directories for hot reload
        // This avoids duplicating activities from the current loaded assemblies
        var packageAssemblies = ScanForPackageAssemblies();
        
        foreach (var assembly in packageAssemblies)
        {
            try
            {
                if (ShouldScanAssembly(assembly))
                {
                    var assemblyActivities = ExtractActivitiesFromAssembly(assembly);
                    activities.AddRange(assemblyActivities);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to scan assembly for activities: {AssemblyName}", assembly.GetName().Name);
            }
        }
        
        // If no external activities found, fall back to local activities
        if (!activities.Any())
        {
            _logger.LogInformation("No external activities found via hot reload, falling back to local activities");
            // This will be handled by the calling service
        }
        
        return activities;
    }

    private IEnumerable<Assembly> ScanForPackageAssemblies()
    {
        var assemblies = new List<Assembly>();
        var packagePaths = GetPackageAssemblyPaths();
        
        foreach (var assemblyPath in packagePaths)
        {
            try
            {
                // Create a new collectible load context for each assembly
                var contextName = $"HotReload_{Path.GetFileNameWithoutExtension(assemblyPath)}_{DateTime.UtcNow.Ticks}";
                var loadContext = new AssemblyLoadContext(contextName, isCollectible: true);
                
                var assembly = loadContext.LoadFromAssemblyPath(assemblyPath);
                
                _loadContexts[assemblyPath] = loadContext;
                _loadedAssemblies[assemblyPath] = new WeakReference(assembly);
                
                assemblies.Add(assembly);
                
                _logger.LogDebug("Loaded assembly from package: {Path}", assemblyPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load assembly from path: {Path}", assemblyPath);
            }
        }
        
        return assemblies;
    }

    private static IEnumerable<string> GetPackageAssemblyPaths()
    {
        var paths = new List<string>();
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        
        var searchPaths = new[]
        {
            Path.Combine(userProfile, ".nuget", "packages"),
            "/root/.nuget/packages",
            AppDomain.CurrentDomain.BaseDirectory
        };

        foreach (var searchPath in searchPaths.Where(Directory.Exists))
        {
            try
            {
                var dllFiles = Directory.GetFiles(searchPath, "*.dll", SearchOption.AllDirectories)
                    .Where(path => ShouldIncludeAssemblyPath(path));
                    
                paths.AddRange(dllFiles);
            }
            catch (Exception)
            {
                // Ignore directory access errors
            }
        }
        
        return paths.Distinct();
    }

    private static bool ShouldIncludeAssemblyPath(string path)
    {
        var fileName = Path.GetFileName(path);
        
        // Skip native DLLs
        if (path.Contains("/native/") || path.Contains("\\native\\"))
            return false;
            
        // Only include assemblies that might contain activities
        var includePatterns = new[] { "temporal", "activities", "workflow" };
        var excludePatterns = new[] { "system.", "microsoft.", "netstandard", "temporalio.dll", "temporal_sdk_bridge" };
        
        var shouldInclude = includePatterns.Any(pattern => 
            fileName.Contains(pattern, StringComparison.OrdinalIgnoreCase));
            
        var shouldExclude = excludePatterns.Any(pattern => 
            fileName.StartsWith(pattern, StringComparison.OrdinalIgnoreCase));
            
        return shouldInclude && !shouldExclude;
    }

    private bool ShouldScanAssembly(Assembly assembly)
    {
        var name = assembly.GetName().Name;
        if (name == null) return false;
        
        // Skip system assemblies but include activity assemblies
        var skipPatterns = new[] { "System.", "Microsoft.", "netstandard", "mscorlib" };
        var includePatterns = new[] { "temporal", "activities", "workflow" };
        
        var shouldSkip = skipPatterns.Any(pattern => name.StartsWith(pattern, StringComparison.OrdinalIgnoreCase));
        var shouldInclude = includePatterns.Any(pattern => name.Contains(pattern, StringComparison.OrdinalIgnoreCase));
        
        return !shouldSkip || shouldInclude;
    }

    private IEnumerable<Delegate> ExtractActivitiesFromAssembly(Assembly assembly)
    {
        var activities = new List<Delegate>();
        
        try
        {
            foreach (var type in assembly.GetTypes())
            {
                var activityMethods = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .Where(m => m.GetCustomAttribute<ActivityAttribute>() != null);
                    
                foreach (var method in activityMethods)
                {
                    try
                    {
                        var activityDelegate = CreateActivityDelegate(method);
                        if (activityDelegate != null)
                        {
                            activities.Add(activityDelegate);
                            _logger.LogInformation("Discovered activity: {ActivityName} from {AssemblyName}", 
                                method.Name, assembly.GetName().Name);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to create delegate for activity method {MethodName}", method.Name);
                    }
                }
            }
        }
        catch (ReflectionTypeLoadException ex)
        {
            _logger.LogWarning("Could not load all types from assembly {AssemblyName}: {LoaderExceptions}", 
                assembly.GetName().Name, string.Join(", ", ex.LoaderExceptions.Select(e => e?.Message)));
        }
        
        return activities;
    }

    private static Delegate? CreateActivityDelegate(MethodInfo method)
    {
        var parameters = method.GetParameters();
        var parameterTypes = parameters.Select(p => p.ParameterType).ToArray();
        var returnType = method.ReturnType;
        
        Type? delegateType = null;
        
        if (returnType == typeof(void))
        {
            delegateType = parameterTypes.Length switch
            {
                0 => typeof(Action),
                1 => typeof(Action<>).MakeGenericType(parameterTypes),
                2 => typeof(Action<,>).MakeGenericType(parameterTypes),
                3 => typeof(Action<,,>).MakeGenericType(parameterTypes),
                4 => typeof(Action<,,,>).MakeGenericType(parameterTypes),
                _ => null
            };
        }
        else
        {
            var allTypes = parameterTypes.Concat(new[] { returnType }).ToArray();
            delegateType = allTypes.Length switch
            {
                1 => typeof(Func<>).MakeGenericType(allTypes),
                2 => typeof(Func<,>).MakeGenericType(allTypes),
                3 => typeof(Func<,,>).MakeGenericType(allTypes),
                4 => typeof(Func<,,,>).MakeGenericType(allTypes),
                5 => typeof(Func<,,,,>).MakeGenericType(allTypes),
                _ => null
            };
        }
        
        return delegateType != null ? Delegate.CreateDelegate(delegateType, method) : null;
    }

    private IEnumerable<Type> LoadWorkflowsFromLoadedAssemblies()
    {
        var workflows = new List<Type>();
        
        // Get assemblies from the current load contexts
        foreach (var kvp in _loadedAssemblies)
        {
            if (kvp.Value.Target is Assembly assembly)
            {
                try
                {
                    if (ShouldScanAssembly(assembly))
                    {
                        var assemblyWorkflows = ExtractWorkflowsFromAssembly(assembly);
                        workflows.AddRange(assemblyWorkflows);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to scan assembly for workflows: {AssemblyName}", assembly.GetName().Name);
                }
            }
        }
        
        return workflows;
    }

    private IEnumerable<Type> ExtractWorkflowsFromAssembly(Assembly assembly)
    {
        var workflows = new List<Type>();
        
        try
        {
            foreach (var type in assembly.GetTypes())
            {
                if (IsWorkflowType(type))
                {
                    workflows.Add(type);
                    _logger.LogInformation("Discovered workflow: {WorkflowName} from {AssemblyName}", 
                        type.Name, assembly.GetName().Name);
                }
            }
        }
        catch (ReflectionTypeLoadException ex)
        {
            _logger.LogWarning("Could not load all types from assembly {AssemblyName}: {LoaderExceptions}", 
                assembly.GetName().Name, string.Join(", ", ex.LoaderExceptions.Select(e => e?.Message)));
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

    public void Dispose()
    {
        if (_disposed) return;
        
        _packageWatcher?.Dispose();
        UnloadPreviousContexts();
        
        _disposed = true;
        _logger.LogInformation("Hot reload manager disposed");
    }
}