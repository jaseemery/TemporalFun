# Blue/Green Deployment Guide

This guide provides detailed instructions for using the task queue-based blue/green deployment system with your Temporal worker.

## Overview

The blue/green deployment system uses Temporal's native task queue functionality to route traffic between two identical environments (blue and green). This approach provides zero-downtime deployments without requiring external load balancers or complex infrastructure.

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                    Temporal Server                              │
├─────────────────────────────────────────────────────────────────┤
│  Task Queues:                                                   │
│  ┌─────────────────┐    ┌─────────────────┐                    │
│  │  production     │    │     staging     │                    │
│  │     (blue)      │    │     (green)     │                    │
│  └─────────────────┘    └─────────────────┘                    │
└─────────────────────────────────────────────────────────────────┘
           │                         │
           │                         │
           ▼                         ▼
┌─────────────────┐         ┌─────────────────┐
│Blue Environment │         │Green Environment│
├─────────────────┤         ├─────────────────┤
│ Worker Blue-1   │         │ Worker Green-1  │
│ Worker Blue-2   │         │ Worker Green-2  │
└─────────────────┘         └─────────────────┘
```

## Setup and Configuration

### 1. Initial Setup

Start the active/standby infrastructure:

```bash
./blue-green-deploy.sh start
```

This creates:
- **Active Environment**: 2 workers initially listening to `production` queue
- **Standby Environment**: 2 workers initially listening to `staging` queue
- **Shared Infrastructure**: Temporal server, UI, and PostgreSQL

### 2. Verify Setup

Check the current status:

```bash
./blue-green-deploy.sh status
```

Expected output:
```
=== Deployment Status ===
🟢 LIVE TRAFFIC:     Active environment (production queue)
🟡 STAGING/TESTING:  Standby environment (staging queue)

Task Queue Routing:
  production queue → Active containers  (handling live customers)
  staging queue    → Standby containers (for testing deployments)

Container Status:
[Container status information]
```

### 3. Verify Workers

Confirm workers are listening to correct queues:

```bash
# Check active worker
docker compose -f docker-compose.blue-green.yml logs temporal-worker-blue-1 --tail 3

# Check standby worker
docker compose -f docker-compose.blue-green.yml logs temporal-worker-green-1 --tail 3
```

You should see messages like:
```
Starting Temporal worker with 3 activities and 1 workflows on task queue: production
Starting Temporal worker with 3 activities and 1 workflows on task queue: staging
```

## Deployment Workflow

### Step 1: Deploy to Standby

Deploy your new version to the staging environment:

```bash
./blue-green-deploy.sh deploy-to-staging
```

This will:
- Identify the current staging environment (standby if active is live)
- Rebuild the Docker images with your latest code
- Restart the staging workers with the new version
- Keep the live environment unchanged

### Step 2: Test Standby Environment

Test your new deployment by sending workflows to the staging queue:

```csharp
// Example: Send test workflow to staging environment
var testWorkflowOptions = new WorkflowOptions
{
    Id = "test-workflow-" + Guid.NewGuid(),
    TaskQueue = "staging"  // Goes to staging environment
};

var handle = await client.StartWorkflowAsync(
    (YourWorkflow wf) => wf.ProcessAsync(testData),
    testWorkflowOptions);
```

### Step 3: Switch Traffic

Once you've verified the staging environment is working correctly, switch traffic:

```bash
# If standby environment is ready and you want to make it live
./blue-green-deploy.sh switch-to-standby

# If active environment should handle live traffic again
./blue-green-deploy.sh switch-to-active
```

This will:
- Stop all workers temporarily
- Update task queue assignments
- Restart workers with new configuration
- Switch traffic instantly

### Step 4: Verify Switch

Confirm the switch was successful:

```bash
./blue-green-deploy.sh status
```

Check that the previously staging environment is now handling live traffic.

## Rollback Procedure

If you need to rollback to the previous version:

```bash
# Simply switch back to the previous environment
./blue-green-deploy.sh switch-to-<previous-environment>
```

For example, if you switched to standby and need to rollback to active:

```bash
./blue-green-deploy.sh switch-to-active
```

## Advanced Usage

### Manual Queue Assignment Verification

You can manually check which queue each worker is listening to:

```bash
# Check active worker task queue
docker compose -f docker-compose.blue-green.yml exec temporal-worker-blue-1 printenv TASK_QUEUE

# Check standby worker task queue
docker compose -f docker-compose.blue-green.yml exec temporal-worker-green-1 printenv TASK_QUEUE
```

### Monitoring During Deployment

Monitor worker logs during deployment:

```bash
# Watch active workers
docker compose -f docker-compose.blue-green.yml logs -f temporal-worker-blue-1

# Watch standby workers
docker compose -f docker-compose.blue-green.yml logs -f temporal-worker-green-1
```

### Testing System Integrity

Run the test suite to verify system integrity:

```bash
cd tests
dotnet test --verbosity minimal
```

All tests should pass regardless of which environment is active.

## Application Code Integration

### Sending Workflows to Specific Environments

```csharp
public class WorkflowService
{
    private readonly ITemporalClient _client;
    
    public WorkflowService(ITemporalClient client)
    {
        _client = client;
    }
    
    // Send to active environment (production traffic)
    public async Task<WorkflowHandle> StartProductionWorkflow(object input)
    {
        var options = new WorkflowOptions
        {
            Id = "production-" + Guid.NewGuid(),
            TaskQueue = "production"  // Active environment
        };
        
        return await _client.StartWorkflowAsync(
            (MyWorkflow wf) => wf.ProcessAsync(input),
            options);
    }
    
    // Send to standby environment (testing)
    public async Task<WorkflowHandle> StartTestWorkflow(object input)
    {
        var options = new WorkflowOptions
        {
            Id = "test-" + Guid.NewGuid(),
            TaskQueue = "standby"  // Standby environment
        };
        
        return await _client.StartWorkflowAsync(
            (MyWorkflow wf) => wf.ProcessAsync(input),
            options);
    }
}
```

### Environment-Aware Configuration

You can configure your application to be aware of the blue-green environment:

```csharp
public class EnvironmentAwareService
{
    private readonly string _environment;
    private readonly string _taskQueue;
    
    public EnvironmentAwareService()
    {
        _environment = Environment.GetEnvironmentVariable("ENVIRONMENT") ?? "unknown";
        _taskQueue = Environment.GetEnvironmentVariable("TASK_QUEUE") ?? "default";
    }
    
    public void LogEnvironmentInfo()
    {
        Console.WriteLine($"Running in {_environment} environment on {_taskQueue} queue");
    }
}
```

## File Structure

The active/standby deployment system uses these files:

```
TemporalWorker/
├── docker-compose.blue-green.yml         # Active/standby Docker configuration
├── deployment.sh                          # Deployment management script
├── docker-compose.override.yml           # Generated automatically during switches
├── Dockerfile                            # Worker container definition
└── docs/
    ├── README.md                         # Main documentation
    └── DEPLOYMENT_GUIDE.md              # This guide
```

## Troubleshooting

### Common Issues

1. **Status shows wrong active environment**
   - Check if `docker-compose.override.yml` exists and has correct values
   - Delete the override file and try switching again

2. **Workers don't switch queues after deployment**
   - Restart with override file: `docker compose -f docker-compose.simple-bg.yml -f docker-compose.override.yml up -d`
   - Check container logs for errors

3. **Deployment fails during build**
   - Ensure code compiles locally: `dotnet build`
   - Check Docker logs: `docker compose -f docker-compose.simple-bg.yml logs <container-name>`

4. **Switch appears successful but queues don't change**
   - Verify environment variables: `docker compose exec <container> printenv TASK_QUEUE`
   - Check worker logs to see actual queue assignment

### Debugging Commands

```bash
# Check current container status
docker compose -f docker-compose.blue-green.yml ps

# View all worker logs
docker compose -f docker-compose.blue-green.yml logs

# Check specific worker environment
docker compose -f docker-compose.blue-green.yml exec temporal-worker-blue-1 env | grep TASK_QUEUE

# Restart specific worker
docker compose -f docker-compose.blue-green.yml restart temporal-worker-blue-1

# Force recreation of containers
docker compose -f docker-compose.blue-green.yml up -d --force-recreate
```

## Best Practices

1. **Always test in standby first**: Deploy to standby and test thoroughly before switching traffic
2. **Monitor during switches**: Keep logs open during traffic switches to catch any issues
3. **Keep deployments small**: Smaller, more frequent deployments are safer than large changes
4. **Test rollback procedures**: Practice rollbacks in non-production environments
5. **Verify system integrity**: Run tests after each deployment to ensure nothing is broken
6. **Document changes**: Keep track of what's deployed in each environment

## Production Considerations

1. **Resource allocation**: Ensure enough CPU/memory for both environments
2. **Monitoring**: Set up alerts for container health and worker connectivity
3. **Backup**: Consider backing up the `docker-compose.override.yml` file
4. **Scaling**: You can scale up workers per environment if needed

## Cleanup

To stop all services and clean up:

```bash
./blue-green-deploy.sh stop
```

This will:
- Stop all containers
- Remove the override file
- Keep volumes for data persistence