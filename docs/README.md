# Temporal Worker with JFrog Artifactory Integration

This Temporal worker dynamically loads activities from NuGet packages hosted on a local JFrog Artifactory instance that runs as part of the Docker Compose stack.

## Features

- **🔥 Hot Reload**: Automatically detects new packages and reloads without worker restart
- **Dynamic Activity Loading**: Automatically discovers and registers activities from NuGet packages
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

## Sample Activities

The included sample package (`TemporalActivities.Sample`) provides these activities:
- `ProcessOrder` - Process customer orders
- `ValidatePayment` - Validate payment tokens
- `SendNotification` - Send notifications
- `GenerateReport` - Generate business reports

## Creating Your Own Activity Packages

1. Create a new .NET class library project
2. Add reference to `Temporalio` NuGet package
3. Create static classes with methods decorated with `[Activity]`
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

## Activity Package Requirements

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

## Hot Reload

The worker monitors file system changes and automatically:
1. **Detects** new packages in NuGet cache directories
2. **Unloads** old assemblies using collectible load contexts
3. **Loads** new assemblies with updated activities
4. **Restarts** the worker with new activities (seamless to workflows)

### How It Works
- **PackageWatcher**: Monitors NuGet cache directories for .dll changes
- **HotReloadManager**: Handles assembly loading/unloading with collectible contexts
- **HotReloadWorkerService**: Manages worker lifecycle and restarts

### Configuration
Set `HOT_RELOAD_ENABLED=false` to disable hot reload and use traditional mode.

## Environment Variables

| Variable | Description | Default |
|----------|-------------|---------|
| `TEMPORAL_SERVER` | Temporal server address | `localhost:7233` |
| `TASK_QUEUE` | Task queue name | `default` |
| `ARTIFACTORY_USERNAME` | JFrog username | `admin` |
| `ARTIFACTORY_PASSWORD` | JFrog password/API key | `password` |
| `HOT_RELOAD_ENABLED` | Enable hot reload functionality | `true` |

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