#!/bin/bash

echo "🔄 Testing Workflow Hot Reload Functionality"
echo "============================================="

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m'

# Configuration
NUGET_PACKAGES_DIR="$HOME/.nuget/packages"
TEST_PACKAGE_NAME="TestTemporalWorkflows"
TEST_PACKAGE_VERSION="1.0.0"
TEST_PACKAGE_DIR="$NUGET_PACKAGES_DIR/$TEST_PACKAGE_NAME/$TEST_PACKAGE_VERSION"

# Function to create a test workflow package
create_test_workflow_package() {
    echo -e "${BLUE}📦 Creating test workflow package...${NC}"
    
    # Create package directory structure
    mkdir -p "$TEST_PACKAGE_DIR/lib/net9.0"
    
    # Create a simple workflow class content
    cat > "$TEST_PACKAGE_DIR/lib/net9.0/TestWorkflows.dll.cs" << 'EOF'
using Temporalio.Workflows;

namespace TestTemporalWorkflows
{
    [Workflow("order-processing-workflow")]
    public class OrderProcessingWorkflow
    {
        [WorkflowRun]
        public async Task<string> RunAsync(OrderRequest request)
        {
            // Simulate workflow logic
            await Workflow.DelayAsync(TimeSpan.FromSeconds(1));
            
            var result = await Workflow.ExecuteActivityAsync(
                () => ProcessOrder(request.OrderId),
                new() { ScheduleToCloseTimeout = TimeSpan.FromMinutes(5) }
            );
            
            return result;
        }
        
        [WorkflowSignal]
        public async Task CancelOrderAsync(string reason)
        {
            // Handle cancellation logic
            await Task.CompletedTask;
        }
        
        [WorkflowQuery]
        public string GetStatus() => "Processing";
        
        private static string ProcessOrder(string orderId) => $"Order {orderId} processed";
    }

    [Workflow("payment-workflow")]
    public class PaymentWorkflow
    {
        [WorkflowRun]
        public async Task<bool> RunAsync(PaymentRequest request)
        {
            await Workflow.DelayAsync(TimeSpan.FromMilliseconds(500));
            
            var isValid = await Workflow.ExecuteActivityAsync(
                () => ValidatePayment(request.Amount),
                new() { ScheduleToCloseTimeout = TimeSpan.FromMinutes(2) }
            );
            
            return isValid;
        }
        
        private static bool ValidatePayment(decimal amount) => amount > 0;
    }

    public record OrderRequest(string OrderId, decimal Amount);
    public record PaymentRequest(decimal Amount, string Currency);
}
EOF

    # Create package metadata
    cat > "$TEST_PACKAGE_DIR/$TEST_PACKAGE_NAME.nuspec" << EOF
<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd">
  <metadata>
    <id>$TEST_PACKAGE_NAME</id>
    <version>$TEST_PACKAGE_VERSION</version>
    <title>Test Temporal Workflows</title>
    <authors>Test</authors>
    <description>Test package for Temporal workflows hot reload</description>
    <dependencies>
      <dependency id="Temporalio" version="1.7.0" />
    </dependencies>
  </metadata>
</package>
EOF

    # Touch the DLL file to simulate a real package
    touch "$TEST_PACKAGE_DIR/lib/net9.0/TestWorkflows.dll"
    
    echo -e "${GREEN}✅ Test workflow package created at: $TEST_PACKAGE_DIR${NC}"
}

# Function to modify the workflow package (simulate package update)
modify_workflow_package() {
    echo -e "${BLUE}🔄 Simulating workflow package update...${NC}"
    
    # Add a new workflow to the package
    cat >> "$TEST_PACKAGE_DIR/lib/net9.0/TestWorkflows.dll.cs" << 'EOF'

    [Workflow("notification-workflow")]
    public class NotificationWorkflow
    {
        [WorkflowRun]
        public async Task<string> RunAsync(NotificationRequest request)
        {
            await Workflow.DelayAsync(TimeSpan.FromMilliseconds(200));
            
            var result = await Workflow.ExecuteActivityAsync(
                () => SendNotification(request.Message),
                new() { ScheduleToCloseTimeout = TimeSpan.FromMinutes(1) }
            );
            
            return result;
        }
        
        private static string SendNotification(string message) => $"Sent: {message}";
    }

    public record NotificationRequest(string Message, string Recipient);
EOF

    # Update the DLL timestamp to trigger hot reload
    touch "$TEST_PACKAGE_DIR/lib/net9.0/TestWorkflows.dll"
    
    # Also trigger the application's file watcher
    touch "./bin/Debug/net9.0/TemporalWorker.dll"
    
    echo -e "${GREEN}✅ Workflow package updated - hot reload should trigger${NC}"
}

# Function to test workflow hot reload
test_workflow_hot_reload() {
    echo -e "${BLUE}📋 Test: Workflow Hot Reload${NC}"
    echo "Starting application..."
    
    # Start the app in background, capture output
    dotnet run > workflow_test_output.log 2>&1 &
    APP_PID=$!
    
    # Wait for app to start
    sleep 8
    
    echo "Creating initial workflow package..."
    create_test_workflow_package
    sleep 3
    
    echo "Triggering workflow package update..."
    modify_workflow_package
    sleep 5
    
    echo "Checking for workflow hot reload in logs..."
    
    # Check for workflow-related log messages
    if grep -q "Discovered workflow" workflow_test_output.log; then
        echo -e "${GREEN}✅ Workflow discovery detected${NC}"
    else
        echo -e "${YELLOW}⚠️  Workflow discovery not detected${NC}"
    fi
    
    if grep -q "Activities changed detected\|Workflows changed detected" workflow_test_output.log; then
        echo -e "${GREEN}✅ Hot reload triggered${NC}"
    else
        echo -e "${YELLOW}⚠️  Hot reload not triggered${NC}"
    fi
    
    if grep -q "Worker restarted successfully" workflow_test_output.log; then
        echo -e "${GREEN}✅ Worker restart successful${NC}"
    else
        echo -e "${YELLOW}⚠️  Worker restart not confirmed${NC}"
    fi
    
    # Check for workflow count changes
    WORKFLOW_COUNTS=$(grep -o "Starting Temporal worker with [0-9]* activities and [0-9]* workflows" workflow_test_output.log | grep -o "[0-9]* workflows" || echo "")
    if [ ! -z "$WORKFLOW_COUNTS" ]; then
        echo -e "${GREEN}✅ Workflow counts detected: $(echo $WORKFLOW_COUNTS | tr '\n' ' ')${NC}"
    else
        echo -e "${YELLOW}⚠️  No workflow counts detected${NC}"
    fi
    
    echo "Stopping application..."
    kill -TERM $APP_PID 2>/dev/null || true
    sleep 3
    
    if kill -0 $APP_PID 2>/dev/null; then
        kill -KILL $APP_PID 2>/dev/null || true
    fi
    
    echo -e "${GREEN}✅ Workflow hot reload test completed${NC}"
    echo ""
}

# Function to test multiple workflow changes
test_multiple_workflow_changes() {
    echo -e "${BLUE}📋 Test: Multiple Workflow Changes${NC}"
    echo "Starting application..."
    
    # Start the app in background
    dotnet run > multi_workflow_test.log 2>&1 &
    APP_PID=$!
    
    # Wait for app to start
    sleep 8
    
    echo "Creating initial workflow package..."
    create_test_workflow_package
    sleep 3
    
    echo "Performing multiple workflow updates..."
    for i in {1..3}; do
        echo "  Workflow update $i/3..."
        
        # Add different workflow variants
        cat >> "$TEST_PACKAGE_DIR/lib/net9.0/TestWorkflows.dll.cs" << EOF

    [Workflow("test-workflow-$i")]
    public class TestWorkflow$i
    {
        [WorkflowRun]
        public async Task<string> RunAsync() 
        {
            await Workflow.DelayAsync(TimeSpan.FromMilliseconds(100));
            return "Workflow $i completed";
        }
    }
EOF
        
        touch "$TEST_PACKAGE_DIR/lib/net9.0/TestWorkflows.dll"
        touch "./bin/Debug/net9.0/TemporalWorker.dll"
        
        sleep 4
    done
    
    # Let it process all changes
    sleep 5
    
    echo "Checking multiple workflow reload results..."
    RELOAD_COUNT=$(grep -c "Worker restarted successfully" multi_workflow_test.log || echo "0")
    echo -e "${GREEN}✅ Completed $RELOAD_COUNT successful worker restarts${NC}"
    
    # Check final workflow count
    FINAL_WORKFLOW_COUNT=$(grep "Starting Temporal worker with" multi_workflow_test.log | tail -1 | grep -o "[0-9]* workflows" || echo "0 workflows")
    echo -e "${GREEN}✅ Final state: $FINAL_WORKFLOW_COUNT loaded${NC}"
    
    echo "Stopping application..."
    kill -TERM $APP_PID 2>/dev/null || true
    sleep 3
    
    if kill -0 $APP_PID 2>/dev/null; then
        kill -KILL $APP_PID 2>/dev/null || true
    fi
    
    echo -e "${GREEN}✅ Multiple workflow changes test completed${NC}"
    echo ""
}

# Function to display results
show_results() {
    echo -e "${BLUE}📊 Test Results Summary${NC}"
    echo "======================="
    
    if [ -f "workflow_test_output.log" ]; then
        echo -e "${YELLOW}📋 Single Workflow Hot Reload Test:${NC}"
        echo "- Workflow discovery: $(grep -c "Discovered workflow" workflow_test_output.log || echo "0") workflows found"
        echo "- Hot reload triggers: $(grep -c "Activities changed detected\|Workflows changed detected" workflow_test_output.log || echo "0")"
        echo "- Worker restarts: $(grep -c "Worker restarted successfully" workflow_test_output.log || echo "0")"
        echo ""
    fi
    
    if [ -f "multi_workflow_test.log" ]; then
        echo -e "${YELLOW}📋 Multiple Workflow Changes Test:${NC}"
        echo "- Total reload triggers: $(grep -c "Worker restarted successfully" multi_workflow_test.log || echo "0")"
        echo "- Final workflow count: $(grep "Starting Temporal worker with" multi_workflow_test.log | tail -1 | grep -o "[0-9]* workflows" || echo "0 workflows")"
        echo ""
    fi
    
    echo -e "${GREEN}🎉 Workflow hot reload testing completed!${NC}"
    echo ""
    echo -e "${BLUE}📋 Log files created:${NC}"
    echo "- workflow_test_output.log: Single workflow test output"
    echo "- multi_workflow_test.log: Multiple workflow changes test output"
}

# Function to cleanup test artifacts
cleanup() {
    echo -e "${BLUE}🧹 Cleaning up test artifacts...${NC}"
    
    # Remove test packages
    rm -rf "$TEST_PACKAGE_DIR" 2>/dev/null || true
    
    echo -e "${GREEN}✅ Cleanup completed${NC}"
}

# Main execution
main() {
    echo -e "${YELLOW}🚀 Starting workflow hot reload tests...${NC}"
    echo ""
    
    # Run tests
    test_workflow_hot_reload
    test_multiple_workflow_changes
    
    # Show results
    show_results
    
    # Cleanup
    cleanup
}

# Handle script interruption
trap cleanup EXIT

# Run main function
main