#!/bin/bash

# Setup script for Artifactory NuGet repository
# This script configures a NuGet repository in the local Artifactory instance

ARTIFACTORY_URL="http://localhost:8082"
ADMIN_USER="admin"
ADMIN_PASSWORD="password"

echo "Setting up Artifactory NuGet repository..."

# Wait for Artifactory to be ready
echo "Waiting for Artifactory to start..."
until curl -f -s "$ARTIFACTORY_URL/artifactory/api/system/ping" > /dev/null; do
    echo "Waiting for Artifactory..."
    sleep 10
done

echo "Artifactory is ready!"

# Create NuGet repository configuration
REPO_CONFIG='{
  "key": "nuget-local",
  "rclass": "local",
  "packageType": "nuget",
  "description": "Local NuGet repository for Temporal activities",
  "repoLayoutRef": "nuget-default",
  "maxUniqueSnapshots": 0,
  "enabledChefCookbookFiles": false,
  "forceNugetAuthentication": false
}'

# Create the NuGet repository
echo "Creating NuGet repository..."
curl -X PUT \
  -H "Content-Type: application/json" \
  -u "$ADMIN_USER:$ADMIN_PASSWORD" \
  -d "$REPO_CONFIG" \
  "$ARTIFACTORY_URL/artifactory/api/repositories/nuget-local"

echo ""
echo "Repository creation response received."

# Create a virtual repository that combines local and remote
VIRTUAL_REPO_CONFIG='{
  "key": "nuget",
  "rclass": "virtual",
  "packageType": "nuget",
  "description": "Virtual NuGet repository aggregating local and remote repositories",
  "repositories": ["nuget-local", "nuget.org"],
  "defaultDeploymentRepo": "nuget-local"
}'

echo "Creating virtual NuGet repository..."
curl -X PUT \
  -H "Content-Type: application/json" \
  -u "$ADMIN_USER:$ADMIN_PASSWORD" \
  -d "$VIRTUAL_REPO_CONFIG" \
  "$ARTIFACTORY_URL/artifactory/api/repositories/nuget"

echo ""
echo "Virtual repository creation response received."

# Create nuget.org remote repository
REMOTE_REPO_CONFIG='{
  "key": "nuget.org",
  "rclass": "remote",
  "packageType": "nuget",
  "description": "Proxy to nuget.org",
  "url": "https://api.nuget.org/v3/index.json",
  "enabledChefCookbookFiles": false,
  "forceNugetAuthentication": false
}'

echo "Creating nuget.org remote repository..."
curl -X PUT \
  -H "Content-Type: application/json" \
  -u "$ADMIN_USER:$ADMIN_PASSWORD" \
  -d "$REMOTE_REPO_CONFIG" \
  "$ARTIFACTORY_URL/artifactory/api/repositories/nuget.org"

echo ""
echo "Remote repository creation response received."

echo "Artifactory setup complete!"
echo "NuGet repository available at: $ARTIFACTORY_URL/artifactory/api/nuget/v3/nuget"
echo "Admin UI available at: $ARTIFACTORY_URL"
echo "Default credentials: admin/password"