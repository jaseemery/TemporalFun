#!/bin/bash

echo "🔄 Testing Artifactory Feed Monitoring"
echo "====================================="

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m'

# Configuration
FEED_URL="http://localhost:8082/artifactory/api/nuget/v3/nuget"
USERNAME="admin"
PASSWORD="password"
PACKAGE_NAME="TemporalActivities.Sample"

echo -e "${BLUE}📋 Test Configuration:${NC}"
echo "  Feed URL: $FEED_URL"
echo "  Package: $PACKAGE_NAME"
echo "  Username: $USERNAME"
echo ""

# Function to check if Artifactory is running
check_artifactory() {
    echo -e "${BLUE}🔍 Checking Artifactory availability...${NC}"
    
    if curl -s -f "$FEED_URL/query?q=test" > /dev/null 2>&1; then
        echo -e "${GREEN}✅ Artifactory is accessible${NC}"
        return 0
    else
        echo -e "${RED}❌ Artifactory is not accessible at $FEED_URL${NC}"
        echo "   Make sure Artifactory is running with: docker compose up -d"
        return 1
    fi
}

# Function to search for packages in the feed
search_packages() {
    echo -e "${BLUE}🔍 Searching for packages in feed...${NC}"
    
    local search_url="$FEED_URL/query?q=$PACKAGE_NAME&take=10"
    local response=$(curl -s -u "$USERNAME:$PASSWORD" "$search_url")
    
    if [ $? -eq 0 ]; then
        echo -e "${GREEN}✅ Successfully queried feed${NC}"
        echo "Response preview:"
        echo "$response" | jq -r '.data[]? | "\(.id) - \(.version)"' 2>/dev/null || echo "$response" | head -5
    else
        echo -e "${RED}❌ Failed to query feed${NC}"
        return 1
    fi
}

# Function to test package download
test_package_download() {
    echo -e "${BLUE}📦 Testing package download...${NC}"
    
    # First, try to get package metadata
    local registration_url="$FEED_URL/registration/${PACKAGE_NAME,,}/index.json"
    local metadata=$(curl -s -u "$USERNAME:$PASSWORD" "$registration_url")
    
    if [ $? -eq 0 ] && [ -n "$metadata" ]; then
        echo -e "${GREEN}✅ Package metadata retrieved${NC}"
        
        # Try to extract version (simplified)
        local version=$(echo "$metadata" | jq -r '.items[0].items[0].catalogEntry.version' 2>/dev/null)
        
        if [ -n "$version" ] && [ "$version" != "null" ]; then
            echo "  Found version: $version"
            
            # Test download URL
            local download_url="$FEED_URL/flatcontainer/${PACKAGE_NAME,,}/${version,,}/${PACKAGE_NAME,,}.${version,,}.nupkg"
            echo "  Testing download from: $download_url"
            
            if curl -s -I -u "$USERNAME:$PASSWORD" "$download_url" | grep -q "200 OK"; then
                echo -e "${GREEN}✅ Package is downloadable${NC}"
            else
                echo -e "${YELLOW}⚠️  Package download URL returned non-200 status${NC}"
            fi
        else
            echo -e "${YELLOW}⚠️  Could not extract version from metadata${NC}"
        fi
    else
        echo -e "${YELLOW}⚠️  Package not found or no metadata available${NC}"
    fi
}

# Function to demonstrate feed monitoring configuration
show_configuration() {
    echo -e "${BLUE}⚙️  Feed Monitoring Configuration:${NC}"
    echo ""
    echo "To enable feed monitoring, set these environment variables:"
    echo ""
    echo -e "${YELLOW}# Enable feed monitoring mode${NC}"
    echo "HOT_RELOAD_MODE=ArtifactoryFeed"
    echo ""
    echo -e "${YELLOW}# Feed configuration${NC}"
    echo "ARTIFACTORY_FEED_URL=$FEED_URL"
    echo "ARTIFACTORY_USERNAME=$USERNAME"
    echo "ARTIFACTORY_PASSWORD=$PASSWORD"
    echo "ARTIFACTORY_POLL_INTERVAL_SECONDS=30"
    echo "ARTIFACTORY_PACKAGE_FILTERS=$PACKAGE_NAME,MyWorkflows"
    echo ""
    echo -e "${YELLOW}# Optional: Custom download path${NC}"
    echo "ARTIFACTORY_DOWNLOAD_PATH=/tmp/TemporalWorker/FeedPackages"
    echo ""
}

# Function to show comparison with file system monitoring
show_comparison() {
    echo -e "${BLUE}📊 Feed vs File System Monitoring:${NC}"
    echo ""
    echo -e "${GREEN}Feed Monitoring Advantages:${NC}"
    echo "  ✅ Works with remote Artifactory instances"
    echo "  ✅ More reliable than file system notifications"
    echo "  ✅ Access to package metadata and versions"
    echo "  ✅ Automatic package download and extraction"
    echo "  ✅ Built-in authentication support"
    echo "  ✅ Package filtering by name/pattern"
    echo ""
    echo -e "${GREEN}File System Monitoring Advantages:${NC}"
    echo "  ✅ Immediate detection (lower latency)"
    echo "  ✅ Works offline"
    echo "  ✅ Monitors any directory structure"
    echo "  ✅ No network dependencies"
    echo ""
}

# Function to run worker with feed monitoring
run_feed_monitoring_test() {
    echo -e "${BLUE}🚀 Running worker with feed monitoring...${NC}"
    echo ""
    echo "This will start the worker in feed monitoring mode."
    echo "Upload a package to Artifactory to test automatic detection."
    echo ""
    echo -e "${YELLOW}Press Ctrl+C to stop the test${NC}"
    echo ""
    
    # Set environment variables for feed monitoring
    export HOT_RELOAD_MODE=ArtifactoryFeed
    export ARTIFACTORY_FEED_URL="$FEED_URL"
    export ARTIFACTORY_USERNAME="$USERNAME"
    export ARTIFACTORY_PASSWORD="$PASSWORD"
    export ARTIFACTORY_POLL_INTERVAL_SECONDS=10
    export ARTIFACTORY_PACKAGE_FILTERS="$PACKAGE_NAME"
    
    # Run the worker
    dotnet run --project . || echo -e "${RED}❌ Failed to start worker${NC}"
}

# Main execution
main() {
    case "${1:-test}" in
        "check")
            check_artifactory
            ;;
        "search")
            check_artifactory && search_packages
            ;;
        "download")
            check_artifactory && test_package_download
            ;;
        "config")
            show_configuration
            ;;
        "compare")
            show_comparison
            ;;
        "run")
            if check_artifactory; then
                run_feed_monitoring_test
            fi
            ;;
        "test"|*)
            echo -e "${YELLOW}🧪 Running comprehensive feed monitoring test...${NC}"
            echo ""
            
            if check_artifactory; then
                search_packages
                echo ""
                test_package_download
                echo ""
                show_configuration
                echo ""
                show_comparison
                echo ""
                echo -e "${GREEN}✅ Feed monitoring test completed${NC}"
                echo ""
                echo -e "${BLUE}Next steps:${NC}"
                echo "  1. Run: $0 run    # Test with actual worker"
                echo "  2. Upload packages to Artifactory"
                echo "  3. Watch automatic detection and loading"
            else
                echo ""
                echo -e "${RED}❌ Feed monitoring test failed${NC}"
                echo "   Please ensure Artifactory is running and accessible"
            fi
            ;;
    esac
}

# Display usage if help requested
if [ "$1" = "-h" ] || [ "$1" = "--help" ]; then
    echo "Usage: $0 [option]"
    echo ""
    echo "Options:"
    echo "  test        Run comprehensive test (default)"
    echo "  check       Check Artifactory connectivity"
    echo "  search      Search for packages in feed"
    echo "  download    Test package download capability"
    echo "  config      Show configuration examples"
    echo "  compare     Show feed vs file system comparison"
    echo "  run         Run worker with feed monitoring"
    echo "  -h, --help  Show this help message"
    echo ""
    echo "Examples:"
    echo "  $0              # Run comprehensive test"
    echo "  $0 check        # Check if Artifactory is accessible"
    echo "  $0 run          # Test with actual worker"
    exit 0
fi

# Run main function
main "$@"