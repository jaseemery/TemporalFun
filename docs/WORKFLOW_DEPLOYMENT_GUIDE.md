# Workflow Deployment Guide

A comprehensive guide for safely deploying new workflows using the blue/green deployment system.

## Overview

This guide walks through the complete process of adding a new workflow to the Temporal worker using our task queue-based blue/green deployment approach. This method ensures zero-downtime deployments and provides safe testing capabilities.

## Deployment Architecture

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
│Blue Environment │         │Green Environment │
│ Current Version │         │ New Version     │
│ (2 workers)     │         │ (2 workers)     │
└─────────────────┘         └─────────────────┘
```

## Step-by-Step Workflow Deployment

### Example Scenario: Adding CustomerOnboardingWorkflow

We'll walk through adding a new workflow that handles customer onboarding with email notifications and database operations.

### Step 1: Develop the New Workflow Locally

Create the new workflow file:

```bash
# Create new workflow file
touch Workflows/CustomerOnboardingWorkflow.cs
```

Implement the workflow:

```csharp
// Workflows/CustomerOnboardingWorkflow.cs
using Temporalio.Workflows;

namespace TemporalWorkerApp.Workflows;

[Workflow("customer-onboarding")]
public class CustomerOnboardingWorkflow
{
    [WorkflowRun]
    public async Task<string> RunAsync(CustomerOnboardingRequest request)
    {
        // Send welcome email
        var emailResult = await Workflow.ExecuteActivityAsync(
            () => EmailActivity.SendEmail(
                request.Email, 
                "Welcome to Our Platform!", 
                $"Welcome {request.Name}! Your account has been created."
            ),
            new() { ScheduleToCloseTimeout = TimeSpan.FromMinutes(5) }
        );

        // Create customer record in database
        var customerData = new Dictionary<string, object>
        {
            ["customerId"] = request.CustomerId,
            ["name"] = request.Name,
            ["email"] = request.Email,
            ["status"] = "active",
            ["createdAt"] = DateTime.UtcNow,
            ["emailSent"] = true
        };

        await Workflow.ExecuteActivityAsync(
            () => DatabaseActivity.SaveData("customers", customerData),
            new() { ScheduleToCloseTimeout = TimeSpan.FromMinutes(5) }
        );

        return $"Customer {request.Name} onboarded successfully with ID: {request.CustomerId}";
    }

    [WorkflowSignal]
    public async Task UpdateCustomerStatusAsync(string newStatus)
    {
        // Handle status updates during onboarding
        await Task.CompletedTask;
    }

    [WorkflowQuery]
    public string GetOnboardingStatus() => "In Progress";
}

// Request record for the workflow
public record CustomerOnboardingRequest(
    string CustomerId, 
    string Name, 
    string Email
);
```

### Step 2: Register the New Workflow

Update `Program.cs` to include the new workflow:

```csharp
// Program.cs - Update the workflows array
var workflows = new Type[]
{
    typeof(TemporalWorkerApp.Workflows.SimpleWorkflow),
    typeof(TemporalWorkerApp.Workflows.CustomerOnboardingWorkflow)  // ← Add new workflow
};

Console.WriteLine($"Starting Temporal worker with {activities.Length} activities and {workflows.Length} workflows on task queue: {taskQueue}");
```

### Step 3: Test Locally

Validate the implementation locally before deployment:

```bash
# Build the project
dotnet build

# Run tests to ensure nothing is broken
dotnet test

# Start the worker locally to verify registration
dotnet run
```

Expected output:
```
Starting Temporal worker with 3 activities and 2 workflows on task queue: default
```

### Step 4: Check Current Deployment Status

Before deploying, check which environment is currently handling live traffic:

```bash
./blue-green-deploy.sh status
```

Expected output:
```
=== Blue-Green Deployment Status ===
🔵 LIVE TRAFFIC:     Blue environment (production queue)
🟢 STAGING/TESTING:  Green environment (staging queue)

Task Queue Routing:
  production queue → Blue containers  (handling live customers)
  staging queue    → Green containers (for testing deployments)

Container Status:
[Container status information]
```

### Step 5: Deploy to Green Environment

Deploy the new workflow to the staging environment:

```bash
./blue-green-deploy.sh deploy-to-staging
```

This will:
1. Identify the current staging environment (green in this example)
2. Rebuild Docker images with the new workflow
3. Restart staging workers with the updated code
4. Keep the live environment unchanged

Expected output:
```
[INFO] Current LIVE environment: blue
[INFO] Deploying new version to STAGING environment: green
[INFO] Rebuilding green environment...

# Docker build process...

[INFO] New version deployed to green environment
[INFO] To switch live traffic, run: ./blue-green-deploy.sh switch-to-green
```

### Step 6: Verify Deployment in Green

Check that the staging environment has the new workflow:

```bash
# Check green worker logs
docker compose -f docker-compose.blue-green.yml logs temporal-worker-green-1 --tail 5
```

Expected output:
```
temporal-worker-green-1  | Starting Temporal worker with 3 activities and 2 workflows on task queue: staging
```

### Step 7: Test the New Workflow in Green

Create a test script to verify the new workflow works correctly:

```csharp
// TestCustomerOnboarding.cs
using Temporalio.Client;

public class WorkflowTester
{
    public static async Task TestCustomerOnboardingAsync()
    {
        var client = await TemporalClient.ConnectAsync(new("localhost:7233"));

        // Send test workflow to STAGING queue (green environment)
        var testOptions = new WorkflowOptions
        {
            Id = "test-customer-onboarding-" + Guid.NewGuid(),
            TaskQueue = "staging"  // ← Routes to green environment with new code
        };

        var testRequest = new CustomerOnboardingRequest(
            CustomerId: "test-" + Guid.NewGuid().ToString("N")[..8],
            Name: "Test Customer", 
            Email: "test@example.com"
        );

        Console.WriteLine("Starting test workflow in green environment...");
        
        var handle = await client.StartWorkflowAsync(
            (CustomerOnboardingWorkflow wf) => wf.RunAsync(testRequest),
            testOptions
        );

        var result = await handle.GetResultAsync();
        Console.WriteLine($"✅ Test completed successfully: {result}");
        
        // Query the workflow status
        var status = await handle.QueryAsync(wf => wf.GetOnboardingStatus());
        Console.WriteLine($"📊 Workflow status: {status}");
    }
}
```

Run the test:

```bash
# Simple test using dotnet script
echo 'using Temporalio.Client; var client = await TemporalClient.ConnectAsync(new("localhost:7233")); await client.StartWorkflowAsync("customer-onboarding", new[] { "test" }, new WorkflowOptions { Id = "test-" + DateTimeOffset.Now.ToUnixTimeSeconds(), TaskQueue = "staging" }); Console.WriteLine("Test workflow started on staging");' > test-staging.cs
dotnet script test-staging.cs
```

### Step 8: Monitor Test Execution

Watch the staging environment logs during testing:

```bash
# Monitor green worker activity
docker compose -f docker-compose.blue-green.yml logs -f temporal-worker-green-1
```

Expected log output:
```
temporal-worker-green-1  | Workflow CustomerOnboardingWorkflow started for customer: test-12345678
temporal-worker-green-1  | Activity EmailActivity.SendEmail starting...
temporal-worker-green-1  | Activity EmailActivity.SendEmail completed successfully
temporal-worker-green-1  | Activity DatabaseActivity.SaveData starting...
temporal-worker-green-1  | Activity DatabaseActivity.SaveData completed successfully
temporal-worker-green-1  | Workflow CustomerOnboardingWorkflow completed: Customer Test Customer onboarded successfully
```

### Step 9: Switch Traffic to New Version

Once testing is successful, switch production traffic to the new version:

```bash
./blue-green-deploy.sh switch-to-green
```

Expected output:
```
[INFO] Switching to GREEN environment (production traffic)
[INFO] 🟢 GREEN environment now handling LIVE TRAFFIC (production queue)
[INFO] 🔵 BLUE environment ready for testing (staging queue)

# Workers restart with new queue assignments
```

### Step 10: Verify Production Traffic Switch

Confirm the switch was successful:

```bash
./blue-green-deploy.sh status
```

Expected output:
```
=== Blue-Green Deployment Status ===
Current Active Environment: green

Task Queue Assignment:
  Blue (Staging):  staging queue       ← Old version available for rollback
  Green (Live):    production queue    ← New version handling live traffic
```

### Step 11: Test Production Workflow

Send a real workflow to the production queue:

```csharp
public static async Task TestProductionWorkflowAsync()
{
    var client = await TemporalClient.ConnectAsync(new("localhost:7233"));

    // Send workflow to PRODUCTION queue (now green environment)
    var productionOptions = new WorkflowOptions
    {
        Id = "customer-onboarding-" + Guid.NewGuid(),
        TaskQueue = "production"  // ← Routes to live environment
    };

    var realRequest = new CustomerOnboardingRequest(
        CustomerId: "CUST-" + Guid.NewGuid().ToString("N")[..8].ToUpper(),
        Name: "Jane Doe", 
        Email: "jane.doe@example.com"
    );

    Console.WriteLine("Starting production workflow...");
    
    var handle = await client.StartWorkflowAsync(
        (CustomerOnboardingWorkflow wf) => wf.RunAsync(realRequest),
        productionOptions
    );

    var result = await handle.GetResultAsync();
    Console.WriteLine($"✅ Production workflow completed: {result}");
}
```

## Rollback Procedure

If issues are discovered after switching traffic, you can instantly rollback:

### Immediate Rollback

```bash
# Switch back to previous environment
./blue-green-deploy.sh switch-to-blue
```

This will:
1. Immediately route production traffic back to blue environment (old code)
2. Keep green environment available for debugging
3. Provide zero-downtime rollback

### Verify Rollback

```bash
./blue-green-deploy.sh status
```

Expected output:
```
=== Blue-Green Deployment Status ===
Current Active Environment: blue

Task Queue Assignment:
  Blue (Active):   production queue    ← Back to old version
  Green (Staging): staging queue       ← New version available for debugging
```

## Advanced Deployment Patterns

### Canary Deployment Simulation

While our system doesn't natively support percentage-based traffic splitting, you can simulate canary deployments:

```csharp
// Route small percentage of workflows to staging for testing
public static async Task CanaryTestAsync()
{
    var client = await TemporalClient.ConnectAsync(new("localhost:7233"));
    
    for (int i = 0; i < 100; i++)
    {
        // Route 10% to staging (canary), 90% to production
        var taskQueue = (i % 10 == 0) ? "staging" : "production";
        
        var options = new WorkflowOptions
        {
            Id = $"workflow-{i}",
            TaskQueue = taskQueue
        };
        
        await client.StartWorkflowAsync(
            (CustomerOnboardingWorkflow wf) => wf.RunAsync(generateTestRequest()),
            options
        );
    }
}
```

### Feature Flag Integration

Combine with feature flags for more control:

```csharp
[Workflow("customer-onboarding")]
public class CustomerOnboardingWorkflow
{
    [WorkflowRun]
    public async Task<string> RunAsync(CustomerOnboardingRequest request)
    {
        // Check feature flag to enable new functionality
        var useNewOnboardingFlow = Environment.GetEnvironmentVariable("ENABLE_NEW_ONBOARDING") == "true";
        
        if (useNewOnboardingFlow)
        {
            return await NewOnboardingFlowAsync(request);
        }
        else
        {
            return await StandardOnboardingFlowAsync(request);
        }
    }
}
```

## Monitoring and Observability

### Key Metrics to Monitor

1. **Workflow Success Rate**
   ```bash
   # Monitor workflow completion rates
   docker compose -f docker-compose.blue-green.yml logs | grep "completed successfully"
   ```

2. **Activity Execution Times**
   ```bash
   # Watch for performance regressions
   docker compose -f docker-compose.blue-green.yml logs | grep "Activity.*completed"
   ```

3. **Error Rates**
   ```bash
   # Monitor for errors in new workflows
   docker compose -f docker-compose.blue-green.yml logs | grep "ERROR\|FAILED"
   ```

### Health Checks

Add health check endpoints to verify workflow registration:

```csharp
// Add to Program.cs
app.MapGet("/health/workflows", () => 
{
    return new
    {
        TotalWorkflows = workflows.Length,
        WorkflowTypes = workflows.Select(w => w.Name).ToArray(),
        Environment = Environment.GetEnvironmentVariable("ENVIRONMENT"),
        TaskQueue = Environment.GetEnvironmentVariable("TASK_QUEUE"),
        Timestamp = DateTime.UtcNow
    };
});
```

## Best Practices

### 1. **Always Test in Standby First**
- Never deploy directly to production
- Thoroughly test all workflow paths in standby
- Verify activity integrations work correctly

### 2. **Monitor During Switches**
- Keep logs open during traffic switches
- Watch for error spikes or performance degradation
- Have rollback plan ready

### 3. **Gradual Rollouts**
- Start with simple test workflows
- Gradually increase complexity and volume
- Monitor each step before proceeding

### 4. **Version Documentation**
- Document what's deployed in each environment
- Track workflow changes and their impacts
- Maintain deployment history

### 5. **Testing Strategy**
```bash
# Comprehensive testing workflow
./scripts/run-tests.sh          # Unit tests
./deploy-to-standby.sh          # Deploy to standby
./test-standby-workflows.sh     # Integration tests
./switch-traffic.sh             # Switch to new version
./monitor-production.sh         # Monitor production
```

## Troubleshooting

### Common Issues

1. **Workflow Not Found**
   ```
   Error: Workflow type 'customer-onboarding' not registered
   ```
   **Solution**: Verify workflow is added to workflows array in Program.cs

2. **Environment Variables Not Updated**
   ```
   Worker still shows old workflow count
   ```
   **Solution**: Restart containers with override file:
   ```bash
   docker compose -f docker-compose.blue-green.yml -f docker-compose.override.yml restart
   ```

3. **Traffic Not Switching**
   ```
   Status shows switch but workers on wrong queues
   ```
   **Solution**: Check override file and restart:
   ```bash
   cat docker-compose.override.yml
   ./blue-green-deploy.sh switch-to-<environment>
   ```

### Debug Commands

```bash
# Check worker environment variables
docker compose -f docker-compose.blue-green.yml exec temporal-worker-blue-1 env | grep TASK_QUEUE

# View recent workflow executions
docker compose -f docker-compose.blue-green.yml logs --since 10m | grep "Workflow.*started\|completed"

# Monitor real-time activity
docker compose -f docker-compose.blue-green.yml logs -f temporal-worker-green-1
```

## Cleanup

After successful deployment and verification:

```bash
# Optional: Clean up test data
# Optional: Archive old deployment logs
# Optional: Update documentation with new workflow details
```

## Summary

This deployment approach provides:

- **Zero Downtime**: Seamless switching between environments
- **Safe Testing**: Full isolation for testing new workflows
- **Instant Rollback**: Quick recovery from issues
- **Production Confidence**: Thorough testing before traffic switch

The blue-green deployment system ensures that new workflows can be safely introduced to production with minimal risk and maximum reliability.