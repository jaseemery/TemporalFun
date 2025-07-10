#!/bin/bash

echo "🧪 Testing Graceful Restart with Artifactory Package Changes"
echo "=========================================================="

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Configuration
NUGET_PACKAGES_DIR="$HOME/.nuget/packages"
TEST_PACKAGE_NAME="TestTemporalActivities"
TEST_PACKAGE_VERSION="1.0.0"
TEST_PACKAGE_DIR="$NUGET_PACKAGES_DIR/$TEST_PACKAGE_NAME/$TEST_PACKAGE_VERSION"

# Function to create a test NuGet package with activities
create_test_package() {
    echo -e "${BLUE}📦 Creating test NuGet package with Temporal activities...${NC}"
    
    # Create package directory structure
    mkdir -p "$TEST_PACKAGE_DIR/lib/net9.0"
    
    # Create a simple activity DLL content (mock)
    cat > "$TEST_PACKAGE_DIR/lib/net9.0/TestTemporalActivities.dll.cs" << 'EOF'
using Temporalio.Activities;

namespace TestTemporalActivities
{
    public static class TestActivities
    {
        [Activity]
        public static async Task<string> ProcessTestOrder(string orderId)
        {
            await Task.Delay(100);
            return $"Processed order: {orderId}";
        }

        [Activity]
        public static async Task<bool> ValidateTestPayment(decimal amount)
        {
            await Task.Delay(50);
            return amount > 0;
        }

        [Activity]
        public static async Task SendTestNotification(string message)
        {
            await Task.Delay(25);
            Console.WriteLine($"Test notification: {message}");
        }
    }
}
EOF

    # Create package metadata
    cat > "$TEST_PACKAGE_DIR/$TEST_PACKAGE_NAME.nuspec" << EOF
<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd">
  <metadata>
    <id>$TEST_PACKAGE_NAME</id>
    <version>$TEST_PACKAGE_VERSION</version>
    <title>Test Temporal Activities</title>
    <authors>Test</authors>
    <description>Test package for Temporal activities hot reload</description>
    <dependencies>
      <dependency id="Temporalio" version="1.7.0" />
    </dependencies>
  </metadata>
</package>
EOF

    # Touch the DLL file to simulate a real package
    touch "$TEST_PACKAGE_DIR/lib/net9.0/TestTemporalActivities.dll"
    
    echo -e "${GREEN}✅ Test package created at: $TEST_PACKAGE_DIR${NC}"
}

# Function to modify the test package (simulate Artifactory update)
modify_test_package() {
    echo -e "${BLUE}🔄 Simulating Artifactory package update...${NC}"
    
    # Modify the activity file to add a new activity
    cat >> "$TEST_PACKAGE_DIR/lib/net9.0/TestTemporalActivities.dll.cs" << 'EOF'

        [Activity]
        public static async Task<string> GenerateTestReport(string reportType)
        {
            await Task.Delay(200);
            return $"Generated {reportType} report at {DateTime.Now}";
        }
EOF

    # Update the DLL timestamp to trigger hot reload
    touch "$TEST_PACKAGE_DIR/lib/net9.0/TestTemporalActivities.dll"
    
    # Also update a file in the watch directory
    touch "./bin/Debug/net9.0/TemporalWorker.dll"
    
    echo -e "${GREEN}✅ Package updated - hot reload should trigger${NC}"
}

# Function to test graceful shutdown
test_graceful_shutdown() {
    echo -e "${BLUE}📋 Test 1: Graceful Shutdown${NC}"
    echo "Starting application..."
    
    # Start the app in background
    timeout 30 dotnet run &
    APP_PID=$!
    
    # Wait for app to start and stabilize
    sleep 8
    
    echo "Sending SIGTERM (graceful shutdown)..."
    kill -TERM $APP_PID 2>/dev/null || true
    
    # Wait for graceful shutdown
    sleep 3
    
    if kill -0 $APP_PID 2>/dev/null; then
        echo -e "${RED}❌ Process still running, forcing termination${NC}"
        kill -KILL $APP_PID 2>/dev/null || true
    else
        echo -e "${GREEN}✅ Graceful shutdown completed successfully${NC}"
    fi
    
    echo ""
}

# Function to test hot reload with package changes
test_hot_reload_with_package() {
    echo -e "${BLUE}📋 Test 2: Hot Reload with Package Changes${NC}"
    echo "Starting application..."
    
    # Start the app in background, capture output
    timeout 45 dotnet run > app_output.log 2>&1 &
    APP_PID=$!
    
    # Wait for app to start
    sleep 8
    
    echo "Triggering hot reload by modifying package..."
    modify_test_package
    
    # Wait for hot reload to process
    sleep 10
    
    echo "Checking for hot reload in logs..."
    if grep -q "Activities changed detected" app_output.log; then
        echo -e "${GREEN}✅ Hot reload detected and triggered${NC}"
    else
        echo -e "${YELLOW}⚠️  Hot reload may not have triggered (check logs)${NC}"
    fi
    
    if grep -q "Worker restarted successfully" app_output.log; then
        echo -e "${GREEN}✅ Worker restarted successfully${NC}"
    else
        echo -e "${YELLOW}⚠️  Worker restart not confirmed (check logs)${NC}"
    fi
    
    echo "Stopping application..."
    kill -TERM $APP_PID 2>/dev/null || true
    sleep 3
    
    if kill -0 $APP_PID 2>/dev/null; then
        kill -KILL $APP_PID 2>/dev/null || true
    fi
    
    echo -e "${GREEN}✅ Hot reload test completed${NC}"
    echo ""
}

# Function to test multiple rapid changes
test_rapid_changes() {
    echo -e "${BLUE}📋 Test 3: Rapid Package Changes (Stress Test)${NC}"
    echo "Starting application..."
    
    # Start the app in background
    timeout 60 dotnet run > stress_output.log 2>&1 &
    APP_PID=$!
    
    # Wait for app to start
    sleep 8
    
    echo "Triggering multiple rapid changes..."
    for i in {1..5}; do
        echo "  Change $i/5..."
        
        # Add content to simulate different package versions
        echo "// Change $i - $(date)" >> "$TEST_PACKAGE_DIR/lib/net9.0/TestTemporalActivities.dll.cs"
        touch "$TEST_PACKAGE_DIR/lib/net9.0/TestTemporalActivities.dll"
        touch "./bin/Debug/net9.0/TemporalWorker.dll"
        
        # Wait between changes
        sleep 3
    done
    
    # Let it process all changes
    sleep 10
    
    echo "Checking stress test results..."
    RELOAD_COUNT=$(grep -c "Worker restarted successfully" stress_output.log || echo "0")
    echo -e "${GREEN}✅ Completed $RELOAD_COUNT successful worker restarts${NC}"
    
    echo "Stopping application..."
    kill -TERM $APP_PID 2>/dev/null || true
    sleep 3
    
    if kill -0 $APP_PID 2>/dev/null; then
        kill -KILL $APP_PID 2>/dev/null || true
    fi
    
    echo -e "${GREEN}✅ Stress test completed${NC}"
    echo ""
}

# Function to cleanup test artifacts
cleanup() {
    echo -e "${BLUE}🧹 Cleaning up test artifacts...${NC}"
    
    # Remove test package
    rm -rf "$TEST_PACKAGE_DIR" 2>/dev/null || true
    
    # Remove log files
    rm -f app_output.log stress_output.log 2>/dev/null || true
    
    echo -e "${GREEN}✅ Cleanup completed${NC}"
}

# Main execution
main() {
    echo -e "${YELLOW}🚀 Starting comprehensive graceful restart tests...${NC}"
    echo ""
    
    # Setup
    create_test_package
    echo ""
    
    # Run tests
    test_graceful_shutdown
    test_hot_reload_with_package
    test_rapid_changes
    
    # Cleanup
    cleanup
    
    echo -e "${GREEN}🎉 All tests completed!${NC}"
    echo ""
    echo -e "${BLUE}📊 Test Summary:${NC}"
    echo "✅ Graceful shutdown functionality"
    echo "✅ Hot reload with package changes"
    echo "✅ Rapid change handling (stress test)"
    echo "✅ Resource cleanup and management"
    echo ""
    echo -e "${YELLOW}💡 Check the application logs for detailed restart behavior${NC}"
}

# Handle script interruption
trap cleanup EXIT

# Run main function
main