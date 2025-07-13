# Temporal Worker with Artifactory Feed Monitoring

A Temporal worker that dynamically loads activities and workflows from NuGet packages by monitoring Artifactory feeds. This approach lets you update your worker's behavior without deploying new code or restarting services.

## Features

- **Hot Reload**: Automatically detects new packages and reloads them without restarting the worker
- **Graceful Restart**: Safe worker restarts with proper resource cleanup and timeout handling
- **Workflow Hot Reload**: Dynamic loading of Temporal workflows from NuGet packages
- **Activity Hot Reload**: Dynamic loading of Temporal activities from NuGet packages
- **Comprehensive Testing**: Full test suite with unit, integration, and hot reload tests
- **Artifactory Feed Monitoring**: Monitor NuGet feeds for package updates
- **File System Monitoring**: Watch local NuGet cache directories for changes
- **Authentication Support**: Built-in support for Artifactory authentication
- **Package Filtering**: Monitor specific packages or patterns
- **Fallback Support**: Uses local activities if no external packages are found

## Quick Start

You have three ways to run this worker depending on your setup:

### Option 1: Use with existing Temporal Server

If you already have a Temporal server running somewhere, this is the simplest approach.

#### 1. Set up Environment
```bash
cp .env.example .env
```

#### 2. Configure for your Artifactory instance
Update `.env` with your Artifactory details:
```bash
HOT_RELOAD_MODE=ArtifactoryFeed
ARTIFACTORY_FEED_URL=https://your-company.jfrog.io/artifactory/api/nuget/v3/your-repo
ARTIFACTORY_USERNAME=your-username
ARTIFACTORY_PASSWORD=your-api-key
TEMPORAL_SERVER=your-temporal-server:7233
```

#### 3. Build and Run
```bash
dotnet build
dotnet run
```

### Option 2: Local Development with Docker (Fully Containerized)

If you want to run everything in containers, this option runs both Temporal services and the worker in Docker.

#### 1. Start All Services
```bash
docker compose up -d
```

This starts:
- **Temporal Server** (port 7233)
- **Temporal UI** (port 8080) 
- **PostgreSQL** (port 5432)
- **Temporal Worker** (containerized)

#### 2. Configure Artifactory (Optional)
If you want to use Artifactory feed monitoring instead of file system monitoring, you can update the environment variables in the docker-compose.yml file:

```yaml
environment:
  - HOT_RELOAD_MODE=ArtifactoryFeed
  - ARTIFACTORY_FEED_URL=https://your-company.jfrog.io/artifactory/api/nuget/v3/your-repo
  - ARTIFACTORY_USERNAME=your-username
  - ARTIFACTORY_PASSWORD=your-api-key
```

The worker runs in file system monitoring mode by default, which works well for development since it can detect changes to the application code inside the container.

### Option 3: Mixed Development (Docker Services + Local Worker)

If you want to run Temporal services in Docker but develop the worker locally for easier debugging:

#### 1. Start Temporal Services Only
```bash
# Start only the infrastructure services
docker compose up temporal-db temporal temporal-ui -d
```

#### 2. Run Worker Locally
```bash
cp .env.example .env
# Set TEMPORAL_SERVER=localhost:7233 in .env
dotnet build
dotnet run
```

This approach gives you the convenience of containerized Temporal services while allowing you to debug and modify the worker code directly on your machine.

## Service URLs (Local Development)

| Service | URL |
|---------|-----|
| Temporal UI | http://localhost:8080 |
| Temporal Server | localhost:7233 |

## Feed Monitoring vs File System Monitoring

You have two options for how the worker detects new packages. Most people will want feed monitoring, but file system monitoring can be useful in some scenarios.

### Artifactory Feed Monitoring (Recommended)

This approach polls your Artifactory NuGet feed directly for package updates:

```bash
# Enable feed monitoring
HOT_RELOAD_MODE=ArtifactoryFeed
ARTIFACTORY_FEED_URL=https://your-artifactory.com/artifactory/api/nuget/v3/nuget-repo
ARTIFACTORY_POLL_INTERVAL_SECONDS=30
ARTIFACTORY_PACKAGE_FILTERS=TemporalActivities,MyWorkflows
ARTIFACTORY_USERNAME=your-username
ARTIFACTORY_PASSWORD=your-api-key
```

**Benefits:**
- Works with remote Artifactory instances
- More reliable than file system notifications
- Access to package metadata, versions, and dependencies
- Only loads complete packages, not partial files
- Built-in support for Artifactory authentication
- Monitor specific packages or patterns
- Automatic handling of package versions

### File System Monitoring

This approach watches local NuGet cache directories for changes:

```bash
# Enable file system monitoring  
HOT_RELOAD_MODE=FileSystem
HOT_RELOAD_WATCH_PATHS=/home/user/.nuget/packages,/tmp/packages
HOT_RELOAD_FILE_FILTER=*.dll
HOT_RELOAD_DEBOUNCE_MS=1000
```

**Benefits:**
- Immediate detection of file changes
- No network dependency - works offline
- Can monitor any directory structure
- Works with existing NuGet workflows

## Creating Activity & Workflow Packages

The worker can load any .NET class library that contains properly attributed Temporal activities or workflows.

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

### Building and Publishing
1. Create a new .NET class library project
2. Add reference to `Temporalio` NuGet package
3. Create activities and workflows with proper attributes
4. Build and pack: `dotnet pack -c Release`
5. Upload to Artifactory via UI, CLI, or API

## Architecture Components

The worker is built with several key components that work together:

- **ArtifactoryFeedWatcher**: Monitors Artifactory NuGet feeds for package updates
- **PackageWatcher**: Monitors NuGet cache directories for .dll changes
- **HotReloadManager**: Handles assembly loading/unloading with collectible contexts
- **ActivityLoader**: Dynamically loads and manages Temporal activities
- **WorkflowLoader**: Dynamically loads and manages Temporal workflows
- **HotReloadWorkerService**: Manages worker lifecycle with graceful restarts

## Hot Reload Process

When a new package is detected, the worker goes through this process:

1. **Detects** new packages via Artifactory feed polling or file system monitoring
2. **Downloads** packages automatically from Artifactory feeds (when using feed mode)
3. **Extracts** and processes .nupkg files to discover activities and workflows
4. **Unloads** old assemblies using collectible load contexts
5. **Loads** new assemblies with updated activities and workflows
6. **Restarts** the worker gracefully with new components

## Environment Variables

| Variable | Description | Default |
|----------|-------------|---------|
| `TEMPORAL_SERVER` | Temporal server address | `localhost:7233` |
| `TASK_QUEUE` | Task queue name | `default` |
| `HOT_RELOAD_ENABLED` | Enable hot reload functionality | `true` |
| `HOT_RELOAD_MODE` | Hot reload mode: `FileSystem`, `ArtifactoryFeed`, `Both` | `FileSystem` |
| `ARTIFACTORY_FEED_URL` | Artifactory NuGet feed URL for monitoring | - |
| `ARTIFACTORY_USERNAME` | Artifactory username | - |
| `ARTIFACTORY_PASSWORD` | Artifactory password/API key | - |
| `ARTIFACTORY_POLL_INTERVAL_SECONDS` | Feed polling interval | `30` |
| `ARTIFACTORY_PACKAGE_FILTERS` | Comma-separated package filters | - |
| `ARTIFACTORY_DOWNLOAD_PATH` | Download path for feed packages | `/tmp/TemporalWorker/FeedPackages` |
| `HOT_RELOAD_WATCH_PATHS` | File system paths to monitor | System NuGet cache |
| `HOT_RELOAD_FILE_FILTER` | File filter for monitoring | `*.dll` |
| `HOT_RELOAD_DEBOUNCE_MS` | Debounce delay for file changes | `1000` |

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

## Testing

The project includes a comprehensive test suite that covers the core functionality:

```bash
# Run all tests
dotnet test

# Run specific test categories
dotnet test --filter Category=Unit
dotnet test --filter Category=Integration
dotnet test --filter Category=HotReload
```

## Docker Management

When using the Docker setup, here are some useful commands:

```bash
# Start all services
docker compose up -d

# View logs for all services
docker compose logs

# View logs for specific service
docker compose logs temporal-worker

# Stop all services
docker compose down

# Rebuild and restart worker (after code changes)
docker compose up --build temporal-worker -d

# Start only infrastructure (no worker)
docker compose up temporal-db temporal temporal-ui -d
```

The worker container automatically restarts if it crashes, and includes health monitoring. If you're developing the worker code, you might prefer Option 3 (mixed development) for easier debugging.

## Troubleshooting

### Authentication Issues
- Verify Artifactory credentials in `.env`
- Check if your API key has package read permissions
- Ensure the Artifactory URL is correct

### Package Not Found
- Verify package exists in Artifactory
- Check package version and availability
- Ensure feed URL is correctly configured

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
- Check network connectivity to Artifactory
- Verify authentication credentials
- Review HotReloadWorkerService logs for restart errors
- Ensure package assemblies are not locked by other processes

### Docker Issues
- If containers fail to start, check logs with `docker compose logs <service-name>`
- Ensure Docker has enough memory allocated (Temporal can be resource-intensive)
- If the worker can't connect to Temporal, verify all services are healthy with `docker compose ps`
- For permission issues on mounted volumes, check that Docker has access to the project directory
- If health checks fail, wait a few minutes for services to fully initialize