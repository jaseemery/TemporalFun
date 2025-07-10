#!/bin/bash

echo "🏭 Artifactory Package Update Simulator"
echo "======================================="

# Colors
GREEN='\033[0;32m'
BLUE='\033[0;34m'
YELLOW='\033[1;33m'
NC='\033[0m'

# Configuration
PACKAGE_NAME="YourCompany.Temporal.Activities.Email"
PACKAGE_VERSION="1.0.$(date +%s)" # Use timestamp for unique version
NUGET_DIR="$HOME/.nuget/packages"
PACKAGE_DIR="$NUGET_DIR/$(echo $PACKAGE_NAME | tr '[:upper:]' '[:lower:]')/$PACKAGE_VERSION"

create_realistic_package() {
    echo -e "${BLUE}📦 Creating realistic NuGet package: $PACKAGE_NAME v$PACKAGE_VERSION${NC}"
    
    # Create package structure
    mkdir -p "$PACKAGE_DIR/lib/net9.0"
    mkdir -p "$PACKAGE_DIR/build"
    
    # Create a .NET project for the package
    TEMP_PROJECT_DIR="/tmp/temporal-activities-$(date +%s)"
    mkdir -p "$TEMP_PROJECT_DIR"
    
    # Create project file
    cat > "$TEMP_PROJECT_DIR/YourCompany.Temporal.Activities.Email.csproj" << EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <PackageId>$PACKAGE_NAME</PackageId>
    <PackageVersion>$PACKAGE_VERSION</PackageVersion>
    <Authors>YourCompany</Authors>
    <Description>Email activities for Temporal workflows</Description>
  </PropertyGroup>
  
  <ItemGroup>
    <PackageReference Include="Temporalio" Version="1.7.0" />
  </ItemGroup>
</Project>
EOF

    # Create activity classes
    cat > "$TEMP_PROJECT_DIR/EmailActivities.cs" << 'EOF'
using Temporalio.Activities;

namespace YourCompany.Temporal.Activities.Email
{
    public static class EmailActivities
    {
        [Activity("send-email")]
        public static async Task<bool> SendEmail(string to, string subject, string body)
        {
            Console.WriteLine($"Sending email to {to}: {subject}");
            await Task.Delay(Random.Shared.Next(100, 500)); // Simulate network delay
            return true;
        }

        [Activity("send-bulk-email")]
        public static async Task<int> SendBulkEmail(string[] recipients, string subject, string body)
        {
            Console.WriteLine($"Sending bulk email to {recipients.Length} recipients: {subject}");
            await Task.Delay(Random.Shared.Next(500, 1500));
            return recipients.Length;
        }

        [Activity("validate-email")]
        public static async Task<bool> ValidateEmailAddress(string email)
        {
            await Task.Delay(Random.Shared.Next(50, 200));
            return email.Contains("@") && email.Contains(".");
        }
    }
}
EOF

    # Build the project
    echo -e "${BLUE}🔨 Building package...${NC}"
    cd "$TEMP_PROJECT_DIR"
    dotnet build -c Release --no-restore 2>/dev/null || dotnet build -c Release
    
    # Copy built DLL to package location
    if [ -f "bin/Release/net9.0/YourCompany.Temporal.Activities.Email.dll" ]; then
        cp "bin/Release/net9.0/YourCompany.Temporal.Activities.Email.dll" "$PACKAGE_DIR/lib/net9.0/"
        echo -e "${GREEN}✅ Package DLL created successfully${NC}"
    else
        # Fallback: create a dummy DLL file
        touch "$PACKAGE_DIR/lib/net9.0/YourCompany.Temporal.Activities.Email.dll"
        echo -e "${YELLOW}⚠️  Created placeholder DLL (build failed)${NC}"
    fi
    
    # Create package manifest
    cat > "$PACKAGE_DIR/$PACKAGE_NAME.nuspec" << EOF
<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd">
  <metadata>
    <id>$PACKAGE_NAME</id>
    <version>$PACKAGE_VERSION</version>
    <title>Email Activities for Temporal</title>
    <authors>YourCompany</authors>
    <description>Production-ready email activities for Temporal workflows</description>
    <dependencies>
      <dependency id="Temporalio" version="1.7.0" />
    </dependencies>
  </metadata>
</package>
EOF

    # Cleanup temp project
    rm -rf "$TEMP_PROJECT_DIR"
    
    echo -e "${GREEN}✅ Package created at: $PACKAGE_DIR${NC}"
}

update_package() {
    echo -e "${BLUE}🔄 Simulating Artifactory package update...${NC}"
    
    # Update timestamp to trigger file watcher
    find "$PACKAGE_DIR" -name "*.dll" -exec touch {} \;
    
    # Also trigger the application's file watcher
    if [ -d "./bin/Debug/net9.0" ]; then
        touch "./bin/Debug/net9.0/TemporalWorker.dll"
    fi
    
    echo -e "${GREEN}✅ Package timestamps updated - hot reload should trigger${NC}"
}

update_project_reference() {
    echo -e "${BLUE}📝 Adding package reference to project...${NC}"
    
    # Check if package reference already exists
    if grep -q "$PACKAGE_NAME" TemporalWorker.csproj; then
        echo -e "${YELLOW}⚠️  Package reference already exists${NC}"
    else
        # Add package reference
        sed -i.bak '/<!-- <PackageReference Include="YourCompany.Temporal.Activities.Email"/c\
    <PackageReference Include="'$PACKAGE_NAME'" Version="'$PACKAGE_VERSION'" />' TemporalWorker.csproj
        
        echo -e "${GREEN}✅ Package reference added to project${NC}"
        
        # Restore packages
        echo -e "${BLUE}📦 Restoring NuGet packages...${NC}"
        dotnet restore
    fi
}

simulate_version_update() {
    echo -e "${BLUE}🆙 Simulating version update (new package version)...${NC}"
    
    # Create new version
    NEW_VERSION="1.0.$(date +%s)"
    NEW_PACKAGE_DIR="$NUGET_DIR/$(echo $PACKAGE_NAME | tr '[:upper:]' '[:lower:]')/$NEW_VERSION"
    
    # Copy existing package to new version
    cp -r "$PACKAGE_DIR" "$NEW_PACKAGE_DIR"
    
    # Update version in manifest
    sed -i.bak "s/<version>.*<\/version>/<version>$NEW_VERSION<\/version>/" "$NEW_PACKAGE_DIR/$PACKAGE_NAME.nuspec"
    
    # Touch to trigger update
    touch "$NEW_PACKAGE_DIR/lib/net9.0/"*.dll
    touch "./bin/Debug/net9.0/TemporalWorker.dll"
    
    echo -e "${GREEN}✅ New package version $NEW_VERSION created${NC}"
}

run_comprehensive_test() {
    echo -e "${YELLOW}🧪 Running comprehensive Artifactory simulation test...${NC}"
    echo ""
    
    # Start the application in background
    echo -e "${BLUE}🚀 Starting Temporal Worker...${NC}"
    dotnet run > artifactory_test.log 2>&1 &
    APP_PID=$!
    
    # Wait for startup
    sleep 10
    
    echo -e "${BLUE}📦 Step 1: Creating initial package...${NC}"
    create_realistic_package
    sleep 3
    
    echo -e "${BLUE}🔄 Step 2: Triggering package update...${NC}"
    update_package
    sleep 5
    
    echo -e "${BLUE}📝 Step 3: Adding package to project...${NC}"
    update_project_reference
    sleep 5
    
    echo -e "${BLUE}🆙 Step 4: Simulating version update...${NC}"
    simulate_version_update
    sleep 5
    
    echo -e "${BLUE}📊 Checking results...${NC}"
    
    # Check for hot reload activity
    if grep -q "Activities changed detected" artifactory_test.log; then
        echo -e "${GREEN}✅ Hot reload detected${NC}"
    else
        echo -e "${YELLOW}⚠️  Hot reload not detected${NC}"
    fi
    
    if grep -q "Worker restarted successfully" artifactory_test.log; then
        echo -e "${GREEN}✅ Worker restart successful${NC}"
    else
        echo -e "${YELLOW}⚠️  Worker restart not confirmed${NC}"
    fi
    
    # Show activity count changes
    ACTIVITY_COUNTS=$(grep -o "Starting Temporal worker with [0-9]* activities" artifactory_test.log | grep -o "[0-9]*" || echo "")
    if [ ! -z "$ACTIVITY_COUNTS" ]; then
        echo -e "${GREEN}✅ Activity counts detected: $(echo $ACTIVITY_COUNTS | tr '\n' ' ')${NC}"
    fi
    
    # Graceful shutdown
    echo -e "${BLUE}🛑 Performing graceful shutdown...${NC}"
    kill -TERM $APP_PID 2>/dev/null || true
    sleep 5
    
    if kill -0 $APP_PID 2>/dev/null; then
        echo -e "${YELLOW}⚠️  Forcing termination${NC}"
        kill -KILL $APP_PID 2>/dev/null || true
    else
        echo -e "${GREEN}✅ Graceful shutdown completed${NC}"
    fi
    
    echo -e "${GREEN}🎉 Artifactory simulation test completed!${NC}"
    echo -e "${BLUE}📋 Check 'artifactory_test.log' for detailed output${NC}"
}

cleanup_test_packages() {
    echo -e "${BLUE}🧹 Cleaning up test packages...${NC}"
    
    # Remove test packages
    rm -rf "$NUGET_DIR/$(echo $PACKAGE_NAME | tr '[:upper:]' '[:lower:]')" 2>/dev/null || true
    
    # Remove log files
    rm -f artifactory_test.log 2>/dev/null || true
    
    # Restore original project file if backup exists
    if [ -f "TemporalWorker.csproj.bak" ]; then
        mv "TemporalWorker.csproj.bak" "TemporalWorker.csproj"
    fi
    
    echo -e "${GREEN}✅ Cleanup completed${NC}"
}

# Menu system
show_menu() {
    echo ""
    echo -e "${YELLOW}Choose an option:${NC}"
    echo "1. Create test package"
    echo "2. Update existing package"
    echo "3. Add package to project"
    echo "4. Simulate version update"
    echo "5. Run comprehensive test"
    echo "6. Cleanup test packages"
    echo "7. Exit"
    echo ""
}

# Main execution
if [ "$1" = "--auto" ]; then
    # Auto mode - run comprehensive test
    run_comprehensive_test
else
    # Interactive mode
    while true; do
        show_menu
        read -p "Enter your choice (1-7): " choice
        
        case $choice in
            1) create_realistic_package ;;
            2) update_package ;;
            3) update_project_reference ;;
            4) simulate_version_update ;;
            5) run_comprehensive_test ;;
            6) cleanup_test_packages ;;
            7) echo "Goodbye!"; exit 0 ;;
            *) echo -e "${YELLOW}Invalid option. Please try again.${NC}" ;;
        esac
        
        echo ""
        read -p "Press Enter to continue..."
    done
fi