#!/bin/bash

# Task Queue Based Blue/Green Deployment
# This script switches traffic between blue and green environments by updating task queues

set -e

COMPOSE_FILE="docker-compose.blue-green.yml"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

log_info() {
    echo -e "${GREEN}[INFO]${NC} $1"
}

log_warn() {
    echo -e "${YELLOW}[WARN]${NC} $1"
}

log_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

# Function to get current active environment
get_active_environment() {
    # Check which environment is listening to the 'production' queue
    if docker compose -f $COMPOSE_FILE exec temporal-worker-blue-1 printenv TASK_QUEUE 2>/dev/null | grep -q "production"; then
        echo "blue"
    else
        echo "green"
    fi
}

# Function to switch blue workers to handle production traffic
switch_to_blue() {
    log_info "Switching to BLUE environment (production traffic)"
    
    # Stop all workers
    docker compose -f $COMPOSE_FILE stop temporal-worker-blue-1 temporal-worker-blue-2 temporal-worker-green-1 temporal-worker-green-2
    
    # Create override file: Blue → production, Green → staging
    cat > docker-compose.override.yml << EOF
services:
  temporal-worker-blue-1:
    environment:
      - TASK_QUEUE=production
  temporal-worker-blue-2:
    environment:
      - TASK_QUEUE=production
  temporal-worker-green-1:
    environment:
      - TASK_QUEUE=staging
  temporal-worker-green-2:
    environment:
      - TASK_QUEUE=staging
EOF
    
    # Start workers with new configuration
    docker compose -f $COMPOSE_FILE up -d temporal-worker-blue-1 temporal-worker-blue-2 temporal-worker-green-1 temporal-worker-green-2
    
    log_info "🔵 BLUE environment now handling LIVE TRAFFIC (production queue)"
    log_info "🟢 GREEN environment ready for testing (staging queue)"
}

# Function to switch green workers to handle production traffic
switch_to_green() {
    log_info "Switching to GREEN environment (production traffic)"
    
    # Stop all workers
    docker compose -f $COMPOSE_FILE stop temporal-worker-blue-1 temporal-worker-blue-2 temporal-worker-green-1 temporal-worker-green-2
    
    # Create override file: Green → production, Blue → staging
    cat > docker-compose.override.yml << EOF
services:
  temporal-worker-blue-1:
    environment:
      - TASK_QUEUE=staging
  temporal-worker-blue-2:
    environment:
      - TASK_QUEUE=staging
  temporal-worker-green-1:
    environment:
      - TASK_QUEUE=production
  temporal-worker-green-2:
    environment:
      - TASK_QUEUE=production
EOF
    
    # Start workers with new configuration
    docker compose -f $COMPOSE_FILE up -d temporal-worker-blue-1 temporal-worker-blue-2 temporal-worker-green-1 temporal-worker-green-2
    
    log_info "🟢 GREEN environment now handling LIVE TRAFFIC (production queue)"
    log_info "🔵 BLUE environment ready for testing (staging queue)"
}

# Function to deploy new version to the environment not handling production traffic
deploy_to_staging() {
    local current_live_env=$(get_active_environment)
    local staging_env
    
    if [ "$current_live_env" = "blue" ]; then
        staging_env="green"
    else
        staging_env="blue"
    fi
    
    log_info "Current LIVE environment: $current_live_env"
    log_info "Deploying new version to STAGING environment: $staging_env"
    
    # Rebuild and restart staging environment
    if [ "$staging_env" = "green" ]; then
        log_info "Rebuilding green environment..."
        docker compose -f $COMPOSE_FILE build temporal-worker-green-1 temporal-worker-green-2
        docker compose -f $COMPOSE_FILE up -d temporal-worker-green-1 temporal-worker-green-2
    else
        log_info "Rebuilding blue environment..."
        docker compose -f $COMPOSE_FILE build temporal-worker-blue-1 temporal-worker-blue-2
        docker compose -f $COMPOSE_FILE up -d temporal-worker-blue-1 temporal-worker-blue-2
    fi
    
    log_info "New version deployed to $staging_env environment"
    log_info "To switch live traffic, run: $0 switch-to-$staging_env"
}

# Function to show current status with clear visual indicators
show_status() {
    local current_live_env=$(get_active_environment)
    
    echo "=== Blue-Green Deployment Status ==="
    echo ""
    
    # Show which environment is handling live traffic
    if [ "$current_live_env" = "blue" ]; then
        echo "🔵 LIVE TRAFFIC:     Blue environment (production queue)"
        echo "🟢 STAGING/TESTING:  Green environment (staging queue)"
        echo ""
        echo "Task Queue Routing:"
        echo "  production queue → Blue containers  (handling live customers)"
        echo "  staging queue    → Green containers (for testing deployments)"
    else
        echo "🟢 LIVE TRAFFIC:     Green environment (production queue)"
        echo "🔵 STAGING/TESTING:  Blue environment (staging queue)"
        echo ""
        echo "Task Queue Routing:"
        echo "  production queue → Green containers (handling live customers)"
        echo "  staging queue    → Blue containers  (for testing deployments)"
    fi
    
    echo ""
    echo "Container Status:"
    docker compose -f $COMPOSE_FILE ps temporal-worker-blue-1 temporal-worker-blue-2 temporal-worker-green-1 temporal-worker-green-2
    
    echo ""
    echo "How to deploy:"
    echo "  1. Run: $0 deploy-to-staging"
    echo "  2. Test the staging environment"
    echo "  3. Run: $0 switch-to-<environment>"
}

# Function to start all services
start_services() {
    log_info "Starting all deployment services..."
    docker compose -f $COMPOSE_FILE up -d
    
    # Default to blue environment handling production if no override exists
    if [ ! -f docker-compose.override.yml ]; then
        switch_to_blue
    fi
}

# Function to stop all services
stop_services() {
    log_info "Stopping all deployment services..."
    docker compose -f $COMPOSE_FILE down
    
    # Clean up override file
    if [ -f docker-compose.override.yml ]; then
        rm docker-compose.override.yml
    fi
}

# Function to show detailed worker status
show_worker_status() {
    echo "=== Detailed Worker Status ==="
    echo ""
    
    local blue_queue_1=$(docker compose -f $COMPOSE_FILE exec temporal-worker-blue-1 printenv TASK_QUEUE 2>/dev/null || echo "N/A")
    local blue_queue_2=$(docker compose -f $COMPOSE_FILE exec temporal-worker-blue-2 printenv TASK_QUEUE 2>/dev/null || echo "N/A")
    local green_queue_1=$(docker compose -f $COMPOSE_FILE exec temporal-worker-green-1 printenv TASK_QUEUE 2>/dev/null || echo "N/A")
    local green_queue_2=$(docker compose -f $COMPOSE_FILE exec temporal-worker-green-2 printenv TASK_QUEUE 2>/dev/null || echo "N/A")
    
    echo "Blue Environment Workers:"
    echo "  temporal-worker-blue-1:  queue=$blue_queue_1"
    echo "  temporal-worker-blue-2:  queue=$blue_queue_2"
    echo ""
    echo "Green Environment Workers:"
    echo "  temporal-worker-green-1: queue=$green_queue_1"
    echo "  temporal-worker-green-2: queue=$green_queue_2"
    echo ""
    
    # Show which environment is handling production
    if [ "$blue_queue_1" = "production" ]; then
        echo "🔵 LIVE TRAFFIC: Blue environment workers"
        echo "🟢 STAGING:      Green environment workers"
    elif [ "$green_queue_1" = "production" ]; then
        echo "🟢 LIVE TRAFFIC: Green environment workers" 
        echo "🔵 STAGING:      Blue environment workers"
    else
        echo "❓ STATUS UNCLEAR: Check worker configurations"
    fi
}

# Main script logic
case "$1" in
    "switch-to-blue")
        switch_to_blue
        ;;
    "switch-to-green")
        switch_to_green
        ;;
    "deploy-to-staging")
        deploy_to_staging
        ;;
    "status")
        show_status
        ;;
    "worker-status")
        show_worker_status
        ;;
    "start")
        start_services
        ;;
    "stop")
        stop_services
        ;;
    *)
        echo "Usage: $0 {switch-to-blue|switch-to-green|deploy-to-staging|status|worker-status|start|stop}"
        echo ""
        echo "Blue/Green Deployment Workflow:"
        echo "  1. start                 - Start all services (blue handles production by default)"
        echo "  2. deploy-to-staging     - Deploy new version to staging environment"
        echo "  3. switch-to-<env>       - Switch live traffic to blue or green environment"
        echo "  4. status                - Show current deployment status"
        echo "  5. worker-status         - Show detailed worker queue assignments"
        echo "  6. stop                  - Stop all services"
        echo ""
        echo "Example workflow:"
        echo "  $0 start"
        echo "  $0 deploy-to-staging     # Deploy to staging environment"
        echo "  $0 switch-to-green       # Switch live traffic to green"
        echo "  $0 deploy-to-staging     # Deploy next version to staging"
        echo "  $0 switch-to-blue        # Switch live traffic back to blue"
        echo ""
        echo "Environment Naming:"
        echo "  • BLUE:  Default environment for live traffic"
        echo "  • GREEN: Alternate environment for deployments"
        echo "  • The environment handling 'production' queue serves live traffic"
        echo "  • The environment handling 'staging' queue is used for testing"
        exit 1
        ;;
esac