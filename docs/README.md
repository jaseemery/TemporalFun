# Temporal Worker with Blue/Green Deployment

A Temporal worker application with built-in blue/green deployment capabilities for zero-downtime updates. This system uses Temporal's native task queue functionality to route traffic between two identical environments (blue and green).

## Features

- **Zero Downtime Deployments**: Switch traffic instantly between active and standby environments
- **Task Queue Based**: Uses Temporal's built-in task queue load balancing
- **Easy Rollback**: Switch back to previous environment in seconds
- **Container Ready**: Fully containerized with Docker Compose
- **Health Checks**: Built-in health monitoring and dependency checks
- **Simple Management**: Script-based deployment management

## Quick Start

### Option 1: Use with existing Temporal Server

If you already have a Temporal server running somewhere:

#### 1. Set up Environment
```bash
cp .env.example .env
```

#### 2. Configure for your Temporal server
Update `.env` with your server details:
```bash
TEMPORAL_SERVER=your-temporal-server:7233
TASK_QUEUE=default
ENVIRONMENT=blue
WORKER_IDENTITY=worker-1
```

#### 3. Build and Run
```bash
dotnet build
dotnet run
```

### Option 2: Local Development with Docker

If you want to run everything in containers:

#### 1. Start All Services
```bash
docker compose up -d
```

This starts:
- **Temporal Server** (port 7233)
- **Temporal UI** (port 8080) 
- **PostgreSQL** (port 5432)
- **Temporal Worker** (containerized)

### Option 3: Mixed Development

If you want to run Temporal services in Docker but develop the worker locally:

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

## Service URLs (Local Development)

| Service | URL |
|---------|-----|
| Temporal UI | http://localhost:8080 |
| Temporal Server | localhost:7233 |

## Blue/Green Deployments

The project includes a task queue-based blue/green deployment system that uses Temporal's native functionality to route traffic between environments.

### How It Works

- **Blue Environment**: Two worker containers listening to either `production` or `staging` task queue
- **Green Environment**: Two worker containers listening to either `production` or `staging` task queue  
- **Traffic Routing**: Workflows are started on either the `production` queue (live) or `staging` queue (testing)
- **Zero Downtime**: Switch traffic by changing which environment listens to the `production` queue

### Quick Start

```bash
# Start blue/green infrastructure (blue handling live traffic by default)
./blue-green-deploy.sh start

# Check current status
./blue-green-deploy.sh status

# Deploy new version to staging environment
./blue-green-deploy.sh deploy-to-staging

# Switch traffic to green environment
./blue-green-deploy.sh switch-to-green

# Switch traffic back to blue environment
./blue-green-deploy.sh switch-to-blue

# Stop all services
./blue-green-deploy.sh stop
```

### Files Used

- `docker-compose.blue-green.yml` - Blue/green Docker Compose configuration
- `blue-green-deploy.sh` - Deployment management script
- `docker-compose.override.yml` - Generated automatically to control task queue assignments

### Deployment Workflow

1. **Start with blue live**: Blue environment handles `production` queue, green handles `staging`
2. **Deploy to staging**: Deploy new version to green environment (while blue stays live)
3. **Test staging**: Send test workflows to `staging` queue to verify green environment
4. **Switch traffic**: Move `production` queue to green environment
5. **Repeat**: Next deployment goes to blue environment (now staging)

### Task Queue Usage in Code

```csharp
// Send workflow to live environment (production queue)
var workflowOptions = new WorkflowOptions
{
    Id = "my-workflow",
    TaskQueue = "production"  // Goes to live environment
};

// Send workflow to staging environment for testing
var testOptions = new WorkflowOptions
{
    Id = "test-workflow", 
    TaskQueue = "staging"  // Goes to staging environment
};
```

### Benefits

- **Zero Downtime**: Traffic switches instantly between environments
- **Easy Rollback**: Switch back to previous environment in seconds
- **Testing**: Test new deployments in standby before switching traffic
- **Temporal Native**: Uses Temporal's built-in task queue load balancing
- **No External Dependencies**: No load balancers or proxies required

### Testing Your Blue/Green Setup

```bash
# Verify workers are listening to correct queues
docker compose -f docker-compose.blue-green.yml logs temporal-worker-blue-1 --tail 3
docker compose -f docker-compose.blue-green.yml logs temporal-worker-green-1 --tail 3

# Check environment variables
docker compose -f docker-compose.blue-green.yml exec temporal-worker-blue-1 printenv TASK_QUEUE
docker compose -f docker-compose.blue-green.yml exec temporal-worker-green-1 printenv TASK_QUEUE

# Check detailed worker status
./blue-green-deploy.sh worker-status

# Run tests to verify system integrity
cd tests && dotnet test --verbosity minimal
```

## Environment Variables

| Variable | Description | Default |
|----------|-------------|---------|
| `TEMPORAL_SERVER` | Temporal server address | `localhost:7233` |
| `TASK_QUEUE` | Task queue name (`production` for live, `staging` for testing) | `default` |
| `ENVIRONMENT` | Environment name (blue/green) for deployments | `unknown` |
| `WORKER_IDENTITY` | Unique worker identity for active/standby deployments | `worker-1` |

## Built-in Activities and Workflows

The worker includes sample activities and workflows for demonstration:

### Activities
- `EmailActivity.SendEmail` - Simulates sending emails
- `DatabaseActivity.SaveData` - Simulates saving data to database
- `DatabaseActivity.GetData` - Simulates retrieving data from database

### Workflows
- `SimpleWorkflow` - Example workflow that coordinates activities

## Testing

The project includes a test suite that covers the core functionality:

```bash
# Run all tests
dotnet test

# Run from tests directory
cd tests && dotnet test --verbosity minimal
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

## Architecture

The worker is built with these core components:

- **Activities**: Business logic functions that perform specific tasks
- **Workflows**: Orchestrate activities to implement business processes
- **Worker Service**: Connects to Temporal and executes activities/workflows
- **Health Checks**: Monitor worker and Temporal connectivity
- **Blue/Green System**: Manages zero-downtime deployments

## Troubleshooting

### Worker Issues
- If worker can't connect to Temporal, verify all services are healthy with `docker compose ps`
- Check worker logs with `docker compose logs temporal-worker`
- Ensure Docker has enough memory allocated (Temporal can be resource-intensive)

### Blue/Green Deployment Issues
- If status shows wrong live environment, check if `docker-compose.override.yml` exists and has correct values
- If workers don't switch queues, restart with override file: `docker compose -f docker-compose.blue-green.yml -f docker-compose.override.yml up -d`
- If deployment fails, check container logs: `docker compose -f docker-compose.blue-green.yml logs <container-name>`
- If builds fail during deployment, ensure code compiles locally first: `dotnet build`

### Docker Issues
- If containers fail to start, check logs with `docker compose logs <service-name>`
- For permission issues on mounted volumes, check that Docker has access to the project directory
- If health checks fail, wait a few minutes for services to fully initialize

## Production Considerations

For production deployment, consider:

1. **Resource Allocation**: Ensure enough CPU/memory for both environments
2. **Storage**: Each environment has separate volumes for data
3. **Monitoring**: Set up alerts for container health and worker connectivity
4. **Scaling**: You can scale up workers per environment if needed
5. **Security**: Use proper secrets management for production credentials

For detailed blue/green deployment instructions, see [BLUE_GREEN_DEPLOYMENT.md](BLUE_GREEN_DEPLOYMENT.md).