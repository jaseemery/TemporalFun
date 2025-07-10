#!/bin/bash

echo "🧪 Running TemporalWorker Test Suite"
echo "==================================="

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m'

# Configuration
TEST_PROJECT_DIR="./tests"
RESULTS_DIR="./test-results"

# Function to run tests with coverage
run_tests_with_coverage() {
    echo -e "${BLUE}🏃 Running tests with coverage...${NC}"
    
    # Create results directory
    mkdir -p "$RESULTS_DIR"
    
    # Run tests with coverage
    dotnet test "$TEST_PROJECT_DIR" \
        --configuration Release \
        --logger "trx;LogFileName=test-results.trx" \
        --logger "console;verbosity=detailed" \
        --results-directory "$RESULTS_DIR" \
        --collect:"XPlat Code Coverage" \
        --settings:"$TEST_PROJECT_DIR/coverlet.runsettings" \
        /p:CollectCoverage=true \
        /p:CoverletOutputFormat=cobertura \
        /p:CoverletOutput="$RESULTS_DIR/coverage.cobertura.xml"
    
    local test_exit_code=$?
    
    if [ $test_exit_code -eq 0 ]; then
        echo -e "${GREEN}✅ All tests passed!${NC}"
    else
        echo -e "${RED}❌ Some tests failed!${NC}"
    fi
    
    return $test_exit_code
}

# Function to run specific test categories
run_unit_tests() {
    echo -e "${BLUE}🔬 Running unit tests...${NC}"
    
    dotnet test "$TEST_PROJECT_DIR" \
        --filter "Category=Unit" \
        --logger "console;verbosity=normal"
}

run_integration_tests() {
    echo -e "${BLUE}🔗 Running integration tests...${NC}"
    
    dotnet test "$TEST_PROJECT_DIR" \
        --filter "Category=Integration" \
        --logger "console;verbosity=normal"
}

# Function to run tests by class
run_workflow_tests() {
    echo -e "${BLUE}🔄 Running workflow tests...${NC}"
    
    dotnet test "$TEST_PROJECT_DIR" \
        --filter "ClassName~Workflow" \
        --logger "console;verbosity=normal"
}

run_activity_tests() {
    echo -e "${BLUE}⚡ Running activity tests...${NC}"
    
    dotnet test "$TEST_PROJECT_DIR" \
        --filter "ClassName~Activity" \
        --logger "console;verbosity=normal"
}

run_hotreload_tests() {
    echo -e "${BLUE}🔥 Running hot reload tests...${NC}"
    
    dotnet test "$TEST_PROJECT_DIR" \
        --filter "ClassName~HotReload" \
        --logger "console;verbosity=normal"
}

# Function to generate coverage report
generate_coverage_report() {
    echo -e "${BLUE}📊 Generating coverage report...${NC}"
    
    if command -v reportgenerator &> /dev/null; then
        reportgenerator \
            "-reports:$RESULTS_DIR/coverage.cobertura.xml" \
            "-targetdir:$RESULTS_DIR/coverage-report" \
            "-reporttypes:Html;TextSummary"
        
        echo -e "${GREEN}✅ Coverage report generated at: $RESULTS_DIR/coverage-report/index.html${NC}"
    else
        echo -e "${YELLOW}⚠️  ReportGenerator not found. Install with: dotnet tool install -g dotnet-reportgenerator-globaltool${NC}"
    fi
}

# Function to clean test results
clean_test_results() {
    echo -e "${BLUE}🧹 Cleaning test results...${NC}"
    rm -rf "$RESULTS_DIR"
    echo -e "${GREEN}✅ Test results cleaned${NC}"
}

# Function to build test project
build_tests() {
    echo -e "${BLUE}🔨 Building test project...${NC}"
    
    dotnet build "$TEST_PROJECT_DIR" --configuration Release
    
    local build_exit_code=$?
    
    if [ $build_exit_code -eq 0 ]; then
        echo -e "${GREEN}✅ Test project built successfully${NC}"
    else
        echo -e "${RED}❌ Test project build failed${NC}"
    fi
    
    return $build_exit_code
}

# Function to restore test dependencies
restore_tests() {
    echo -e "${BLUE}📦 Restoring test dependencies...${NC}"
    
    dotnet restore "$TEST_PROJECT_DIR"
    
    local restore_exit_code=$?
    
    if [ $restore_exit_code -eq 0 ]; then
        echo -e "${GREEN}✅ Test dependencies restored${NC}"
    else
        echo -e "${RED}❌ Failed to restore test dependencies${NC}"
    fi
    
    return $restore_exit_code
}

# Function to show test summary
show_test_summary() {
    echo -e "${BLUE}📋 Test Summary${NC}"
    echo "==============="
    
    if [ -f "$RESULTS_DIR/test-results.trx" ]; then
        echo -e "${GREEN}✅ Test results available at: $RESULTS_DIR/test-results.trx${NC}"
    fi
    
    if [ -f "$RESULTS_DIR/coverage.cobertura.xml" ]; then
        echo -e "${GREEN}✅ Coverage report available at: $RESULTS_DIR/coverage.cobertura.xml${NC}"
    fi
    
    if [ -f "$RESULTS_DIR/coverage-report/index.html" ]; then
        echo -e "${GREEN}✅ HTML coverage report available at: $RESULTS_DIR/coverage-report/index.html${NC}"
    fi
}

# Main execution
main() {
    case "${1:-all}" in
        "clean")
            clean_test_results
            ;;
        "restore")
            restore_tests
            ;;
        "build")
            build_tests
            ;;
        "unit")
            build_tests && run_unit_tests
            ;;
        "integration")
            build_tests && run_integration_tests
            ;;
        "workflow")
            build_tests && run_workflow_tests
            ;;
        "activity")
            build_tests && run_activity_tests
            ;;
        "hotreload")
            build_tests && run_hotreload_tests
            ;;
        "coverage")
            build_tests && run_tests_with_coverage && generate_coverage_report
            ;;
        "all"|*)
            echo -e "${YELLOW}🚀 Running complete test suite...${NC}"
            echo ""
            
            restore_tests && \
            build_tests && \
            run_tests_with_coverage
            
            local exit_code=$?
            
            echo ""
            show_test_summary
            
            exit $exit_code
            ;;
    esac
}

# Display usage if help requested
if [ "$1" = "-h" ] || [ "$1" = "--help" ]; then
    echo "Usage: $0 [option]"
    echo ""
    echo "Options:"
    echo "  all         Run all tests (default)"
    echo "  unit        Run unit tests only"
    echo "  integration Run integration tests only"
    echo "  workflow    Run workflow-related tests"
    echo "  activity    Run activity-related tests"
    echo "  hotreload   Run hot reload tests"
    echo "  coverage    Run tests with coverage report"
    echo "  build       Build test project"
    echo "  restore     Restore test dependencies"
    echo "  clean       Clean test results"
    echo "  -h, --help  Show this help message"
    echo ""
    echo "Examples:"
    echo "  $0              # Run all tests"
    echo "  $0 workflow     # Run workflow tests only"
    echo "  $0 coverage     # Run tests with coverage"
    exit 0
fi

# Run main function
main "$@"