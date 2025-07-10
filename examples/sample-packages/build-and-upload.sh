#!/bin/bash

# Script to build and upload sample NuGet package to local Artifactory

set -e

PACKAGE_DIR="TemporalActivities.Sample"
ARTIFACTORY_URL="http://localhost:8082/artifactory/api/nuget/nuget-local"
USERNAME="admin"
PASSWORD="password"

echo "Building sample NuGet package..."

cd "$PACKAGE_DIR"

# Clean previous builds
rm -rf bin/ obj/ *.nupkg

# Build and pack the project
dotnet pack -c Release -o .

# Find the generated package
PACKAGE_FILE=$(find . -name "*.nupkg" -type f | head -n 1)

if [ -z "$PACKAGE_FILE" ]; then
    echo "Error: No .nupkg file found!"
    exit 1
fi

echo "Package created: $PACKAGE_FILE"

# Wait for Artifactory to be ready
echo "Checking if Artifactory is ready..."
until curl -f -s "http://localhost:8082/artifactory/api/system/ping" > /dev/null; do
    echo "Waiting for Artifactory..."
    sleep 5
done

echo "Artifactory is ready!"

# Upload the package to Artifactory
echo "Uploading package to Artifactory..."

curl -u "$USERNAME:$PASSWORD" \
     -X PUT \
     -T "$PACKAGE_FILE" \
     "$ARTIFACTORY_URL/$(basename "$PACKAGE_FILE")"

echo ""
echo "Package uploaded successfully!"
echo "Package available at: $ARTIFACTORY_URL/$(basename "$PACKAGE_FILE")"

cd ..

echo "Sample package build and upload complete!"