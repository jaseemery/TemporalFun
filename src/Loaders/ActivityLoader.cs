using System.Reflection;
using Temporalio.Activities;
using Microsoft.Extensions.Logging;

using TemporalWorkerApp.Managers;
using TemporalWorkerApp.Watchers;

namespace TemporalWorkerApp.Loaders;

public class ActivityLoader : IDisposable
{
    private readonly ILogger<ActivityLoader> _logger;
    private readonly HotReloadManager _hotReloadManager;
    private bool _disposed = false;

    public event Action<IEnumerable<Delegate>>? ActivitiesChanged;
    
    // Expose HotReloadManager for sharing with WorkflowLoader
    public HotReloadManager HotReloadManager => _hotReloadManager;

    public ActivityLoader(ILogger<ActivityLoader> logger, ILogger<HotReloadManager> hotReloadLogger, ILogger<PackageWatcher> packageWatcherLogger)
    {
        _logger = logger;
        _hotReloadManager = new HotReloadManager(hotReloadLogger, packageWatcherLogger);
        _hotReloadManager.ActivitiesReloaded += OnActivitiesReloaded;
    }

    private void OnActivitiesReloaded(IEnumerable<Delegate> newActivities)
    {
        _logger.LogInformation("Activities reloaded via hot reload, notifying worker...");
        ActivitiesChanged?.Invoke(newActivities);
    }

    public static IEnumerable<Delegate> LoadActivitiesFromAssemblies(ILogger logger)
    {
        var activities = new List<Delegate>();
        
        // Get all loaded assemblies in the current app domain
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        
        logger.LogInformation("Scanning {AssemblyCount} assemblies for Temporal activities", assemblies.Length);
        
        foreach (var assembly in assemblies)
        {
            try
            {
                // Skip system assemblies to improve performance
                if (IsSystemAssembly(assembly))
                    continue;
                    
                logger.LogDebug("Scanning assembly: {AssemblyName}", assembly.GetName().Name);
                
                var assemblyActivities = LoadActivitiesFromAssembly(assembly, logger);
                activities.AddRange(assemblyActivities);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to scan assembly {AssemblyName} for activities", 
                    assembly.GetName().Name);
            }
        }
        
        logger.LogInformation("Found {ActivityCount} activities from NuGet packages", activities.Count);
        return activities;
    }

    public async Task<IEnumerable<Delegate>> LoadActivitiesWithHotReloadAsync()
    {
        _logger.LogInformation("Loading activities with hot reload support...");
        return await _hotReloadManager.ReloadActivitiesAsync();
    }
    
    private static IEnumerable<Delegate> LoadActivitiesFromAssembly(Assembly assembly, ILogger logger)
    {
        var activities = new List<Delegate>();
        
        foreach (var type in assembly.GetTypes())
        {
            // Look for static methods with [Activity] attribute
            var activityMethods = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.GetCustomAttribute<ActivityAttribute>() != null);
                
            foreach (var method in activityMethods)
            {
                try
                {
                    // Create delegate for the activity method
                    var activityDelegate = CreateActivityDelegate(method);
                    if (activityDelegate != null)
                    {
                        activities.Add(activityDelegate);
                        logger.LogInformation("Registered activity: {ActivityName} from {AssemblyName}", 
                            method.Name, assembly.GetName().Name);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to register activity method {MethodName} from {TypeName}", 
                        method.Name, type.Name);
                }
            }
        }
        
        return activities;
    }
    
    private static Delegate? CreateActivityDelegate(MethodInfo method)
    {
        // Create a delegate for the static method
        // This handles various method signatures dynamically
        var parameters = method.GetParameters();
        var parameterTypes = parameters.Select(p => p.ParameterType).ToArray();
        var returnType = method.ReturnType;
        
        // Create appropriate delegate type based on parameters and return type
        Type? delegateType;
        
        if (returnType == typeof(void))
        {
            delegateType = parameterTypes.Length switch
            {
                0 => typeof(Action),
                1 => typeof(Action<>).MakeGenericType(parameterTypes),
                2 => typeof(Action<,>).MakeGenericType(parameterTypes),
                3 => typeof(Action<,,>).MakeGenericType(parameterTypes),
                4 => typeof(Action<,,,>).MakeGenericType(parameterTypes),
                _ => null // Add more cases as needed
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
                _ => null // Add more cases as needed
            };
        }
        
        if (delegateType == null)
            return null;
            
        return Delegate.CreateDelegate(delegateType, method);
    }
    
    private static bool IsSystemAssembly(Assembly assembly)
    {
        var name = assembly.GetName().Name;
        if (name == null) return true;
        
        return name.StartsWith("System.") ||
               name.StartsWith("Microsoft.") ||
               name.StartsWith("netstandard") ||
               name.StartsWith("mscorlib") ||
               name == "System" ||
               name.StartsWith("Temporalio") && !name.Contains("Activities");
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        _hotReloadManager?.Dispose();
        _disposed = true;
        
        _logger.LogInformation("Activity loader disposed");
    }
}