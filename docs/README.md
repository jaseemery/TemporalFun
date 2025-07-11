# Temporal Worker with JFrog Artifactory Integration

This Temporal worker dynamically loads activities from NuGet packages hosted on a local JFrog Artifactory instance that runs as part of the Docker Compose stack.

## Features

- **🔥 Hot Reload**: Automatically detects new packages and reloads without worker restart
- **🛡️ Graceful Restart**: Safe worker restarts with proper resource cleanup and timeout handling
- **🔄 Workflow Hot Reload**: Dynamic loading of Temporal workflows from NuGet packages
- **⚡ Activity Hot Reload**: Dynamic loading of Temporal activities from NuGet packages
- **🧪 Comprehensive Testing**: Full test suite with unit, integration, and hot reload tests
- **Local JFrog Artifactory**: Complete Artifactory instance running in Docker
- **Automated Setup**: Scripts to configure repositories and upload sample packages
- **Fallback Support**: Uses local activities if no external packages are found
- **Sample Package**: Includes a sample activity package for testing
- **File System Monitoring**: Watches NuGet cache and package directories for changes

## Quick Start

### 1. Set up Environment
```bash
cp .env.example .env
```
The default credentials work with the local Artifactory instance.

### 2. Start All Services
```bash
docker compose up --build
```

This will start:
- **Temporal Worker** (connects to Artifactory for packages)
- **Temporal Server** (port 7233)
- **Temporal UI** (port 8080)
- **PostgreSQL** (port 5432)
- **Artifactory** (port 8082)
- **Artifactory Database** (port 5433)

### 3. Configure Artifactory (First Time Only)
After all services are running, set up the NuGet repository:
```bash
./scripts/setup-artifactory.sh
```

### 4. Upload Sample Package
Build and upload the sample activity package:
```bash
cd sample-packages
./build-and-upload.sh
```

### 5. Watch Hot Reload in Action
The worker automatically detects new packages and reloads them without restart! Upload new packages and watch the logs:
```bash
docker compose logs -f temporal-worker
```

To manually restart if needed:
```bash
docker compose restart temporal-worker
```

## Service URLs

| Service | URL | Credentials |
|---------|-----|-------------|
| Artifactory UI | http://localhost:8082 | admin/password |
| Temporal UI | http://localhost:8080 | - |
| Temporal Server | localhost:7233 | - |

## Sample Activities & Workflows

The included sample package (`TemporalActivities.Sample`) provides these activities:
- `ProcessOrder` - Process customer orders
- `ValidatePayment` - Validate payment tokens
- `SendNotification` - Send notifications
- `GenerateReport` - Generate business reports

Sample workflows available for hot reload:
- `OrderProcessingWorkflow` - Complete order processing workflow
- `UserOnboardingWorkflow` - User registration and onboarding
- `SimpleWorkflow` - Basic workflow for testing

## Creating Your Own Activity & Workflow Packages

1. Create a new .NET class library project
2. Add reference to `Temporalio` NuGet package
3. Create activities and workflows with proper attributes
4. Build and pack: `dotnet pack -c Release`
5. Upload to Artifactory via UI or API

### Example Activity
```csharp
using Temporalio.Activities;

public static class MyActivities
{
    [Activity]
    public static async Task<string> ProcessData(string input)
    {
        var logger = ActivityExecutionContext.Current.Logger;
        logger.LogInformation("Processing: {Input}", input);
        
        await Task.Delay(1000); // Simulate work
        
        return "processed: " + input;
    }
}
```

### Example Workflow
```csharp
using Temporalio.Workflows;

[Workflow("my-workflow")]
public class MyWorkflow
{
    [WorkflowRun]
    public async Task<string> RunAsync(WorkflowInput input)
    {
        // Execute activities
        var result = await Workflow.ExecuteActivityAsync(
            () => MyActivities.ProcessData(input.Data),
            new() { StartToCloseTimeout = TimeSpan.FromMinutes(5) });
        
        return result;
    }
}
```

## Advanced Configuration

### Custom Artifactory URL
Update `NuGet.Config` to point to your external Artifactory:
```xml
<add key="Artifactory" value="https://your-company.jfrog.io/artifactory/api/nuget/v3/nuget-repo" />
```

### Authentication
Update credentials in `.env`:
```bash
ARTIFACTORY_USERNAME=your-username
ARTIFACTORY_PASSWORD=your-api-key
```

## Package Requirements

### Activity Package Requirements
Activity packages should contain classes with static methods decorated with `[Activity]` attributes:

```csharp
using Temporalio.Activities;

public static class MyActivities
{
    [Activity]
    public static async Task<string> ProcessData(string input)
    {
        // Activity implementation
        return "processed: " + input;
    }
}
```

### Workflow Package Requirements
Workflow packages should contain classes decorated with `[Workflow]` attributes and `[WorkflowRun]` methods:

```csharp
using Temporalio.Workflows;

[Workflow("my-business-workflow")]
public class MyBusinessWorkflow
{
    [WorkflowRun]
    public async Task<Result> RunAsync(Input input)
    {
        // Workflow implementation
        return new Result();
    }
}
```

### Project Structure
For packages containing both activities and workflows:
```
MyTemporalPackage/
├── Activities/
│   ├── ProcessingActivities.cs
│   └── NotificationActivities.cs
├── Workflows/
│   ├── OrderWorkflow.cs
│   └── PaymentWorkflow.cs
└── Models/
    ├── Input.cs
    └── Result.cs
```

## Hot Reload & Graceful Restart

The worker supports dynamic loading of both activities and workflows with safe restart mechanisms:

### Hot Reload Features
1. **Detects** new packages via file system monitoring or Artifactory feed polling
2. **Downloads** packages automatically from Artifactory feeds (when using feed mode)
3. **Extracts** and processes .nupkg files to discover activities and workflows
4. **Unloads** old assemblies using collectible load contexts
5. **Loads** new assemblies with updated activities and workflows
6. **Restarts** the worker gracefully with new components

### Feed Monitoring vs File System Monitoring

#### Artifactory Feed Monitoring Benefits:
- ✅ **Network-aware**: Works with remote Artifactory instances
- ✅ **More reliable**: No dependency on file system notifications
- ✅ **Metadata-rich**: Access to package metadata, versions, and dependencies
- ✅ **Atomic**: Only loads complete packages, not partial files
- ✅ **Authentication**: Built-in support for Artifactory authentication
- ✅ **Filtering**: Monitor specific packages or patterns
- ✅ **Version management**: Automatic handling of package versions

#### File System Monitoring Benefits:
- ✅ **Lower latency**: Immediate detection of file changes
- ✅ **No network dependency**: Works offline
- ✅ **Broader scope**: Can monitor any directory structure
- ✅ **Legacy compatibility**: Works with existing NuGet workflows

### Architecture Components
- **PackageWatcher**: Monitors NuGet cache directories for .dll changes
- **HotReloadManager**: Handles assembly loading/unloading with collectible contexts
- **ActivityLoader**: Dynamically loads and manages Temporal activities
- **WorkflowLoader**: Dynamically loads and manages Temporal workflows
- **HotReloadWorkerService**: Manages worker lifecycle with graceful restarts

### Graceful Restart Features
- **Double Ctrl+C Protection**: First Ctrl+C triggers graceful shutdown, second forces exit
- **Timeout Handling**: 30-second graceful shutdown timeout with force termination fallback
- **Resource Cleanup**: Proper disposal of workers, connections, and managed resources
- **Race Condition Prevention**: Thread-safe restart logic with proper synchronization
- **Comprehensive Logging**: Detailed logging of restart operations and status

### Configuration
- Set `HOT_RELOAD_ENABLED=false` to disable hot reload and use traditional mode
- Set `HOT_RELOAD_MODE=ArtifactoryFeed` to monitor Artifactory feeds instead of file system
- Set `HOT_RELOAD_MODE=FileSystem` for traditional file system monitoring (default)
- Set `HOT_RELOAD_MODE=Both` for hybrid monitoring (prioritizes Artifactory feed)
- Graceful restart is always enabled for worker safety

#### Artifactory Feed Monitoring
```bash
# Enable feed monitoring
HOT_RELOAD_MODE=ArtifactoryFeed
ARTIFACTORY_FEED_URL=http://artifactory:8082/artifactory/api/nuget/v3/nuget
ARTIFACTORY_POLL_INTERVAL_SECONDS=30
ARTIFACTORY_PACKAGE_FILTERS=TemporalActivities,MyWorkflows
```

#### File System Monitoring (Default)
```bash
# Enable file system monitoring  
HOT_RELOAD_MODE=FileSystem
HOT_RELOAD_WATCH_PATHS=/home/user/.nuget/packages,/tmp/packages
HOT_RELOAD_FILE_FILTER=*.dll
HOT_RELOAD_DEBOUNCE_MS=1000
```

### Testing
Run the comprehensive test suite:
```bash
# Run all tests
./scripts/run-tests.sh

# Run specific test categories
./scripts/run-tests.sh unit          # Unit tests only
./scripts/run-tests.sh integration   # Integration tests only
./scripts/run-tests.sh workflow      # Workflow tests only
./scripts/run-tests.sh activity      # Activity tests only
./scripts/run-tests.sh hotreload     # Hot reload tests only
./scripts/run-tests.sh coverage      # Tests with coverage report
```

## Environment Variables

| Variable | Description | Default |
|----------|-------------|---------|
| `TEMPORAL_SERVER` | Temporal server address | `localhost:7233` |
| `TASK_QUEUE` | Task queue name | `default` |
| `ARTIFACTORY_USERNAME` | JFrog username | `admin` |
| `ARTIFACTORY_PASSWORD` | JFrog password/API key | `password` |
| `HOT_RELOAD_ENABLED` | Enable hot reload functionality | `true` |
| `HOT_RELOAD_MODE` | Hot reload mode: `FileSystem`, `ArtifactoryFeed`, `Both` | `FileSystem` |
| `ARTIFACTORY_FEED_URL` | Artifactory NuGet feed URL for monitoring | - |
| `ARTIFACTORY_POLL_INTERVAL_SECONDS` | Feed polling interval | `30` |
| `ARTIFACTORY_PACKAGE_FILTERS` | Comma-separated package filters | - |
| `ARTIFACTORY_DOWNLOAD_PATH` | Download path for feed packages | `/tmp/TemporalWorker/FeedPackages` |
| `HOT_RELOAD_WATCH_PATHS` | File system paths to monitor | System NuGet cache |
| `HOT_RELOAD_FILE_FILTER` | File filter for monitoring | `*.dll` |
| `HOT_RELOAD_DEBOUNCE_MS` | Debounce delay for file changes | `1000` |

## Services

- **Temporal Worker**: Main application (port varies)
- **Temporal Server**: Core Temporal service (port 7233)
- **Temporal UI**: Web interface (port 8080)
- **PostgreSQL**: Database backend (port 5432)

## Troubleshooting

### Authentication Issues
- Verify Artifactory credentials in `.env`
- Check if your API key has package read permissions
- Ensure the Artifactory URL is correct

### Package Not Found
- Verify package exists in Artifactory
- Check package version in `.csproj`
- Ensure package source is correctly configured in `NuGet.Config`

### Activity Not Loading
- Verify activity methods have `[Activity]` attribute
- Check assembly is properly loaded (review logs)
- Ensure activity methods are public and static

### Workflow Not Loading
- Verify workflow classes have `[Workflow]` attribute
- Ensure workflow run method has `[WorkflowRun]` attribute
- Check workflow classes are public and non-static
- Review logs for assembly loading errors

### Hot Reload Issues
- Check file system permissions on NuGet cache directories
- Verify HOT_RELOAD_ENABLED environment variable is set to `true`
- Review HotReloadWorkerService logs for restart errors
- Ensure package assemblies are not locked by other processes

### Test Failures
- Run tests individually to isolate issues: `./scripts/run-tests.sh unit`
- Check test logs for specific error details
- Verify all dependencies are properly restored: `dotnet restore tests/`
- Ensure no processes are locking test assemblies