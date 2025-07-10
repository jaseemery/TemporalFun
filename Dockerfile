FROM mcr.microsoft.com/dotnet/runtime:8.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy build-time NuGet.Config (uses only nuget.org)
COPY ["NuGet.Config.build", "./NuGet.Config"]

# Copy project file
COPY ["TemporalWorker.csproj", "."]

# Restore packages from NuGet.org only during build
RUN dotnet restore "./TemporalWorker.csproj"

# Copy source code
COPY . .
WORKDIR "/src/."

# Build the application
RUN dotnet build "TemporalWorker.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "TemporalWorker.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Copy runtime NuGet.Config (includes Artifactory)
COPY ["NuGet.Config.runtime", "./NuGet.Config"]

# Environment variables for Temporal configuration
ENV TEMPORAL_SERVER=localhost:7233
ENV TASK_QUEUE=default

# Set environment variables for Artifactory credentials
ARG ARTIFACTORY_USERNAME
ARG ARTIFACTORY_PASSWORD
ENV ARTIFACTORY_USERNAME=$ARTIFACTORY_USERNAME
ENV ARTIFACTORY_PASSWORD=$ARTIFACTORY_PASSWORD

ENTRYPOINT ["dotnet", "TemporalWorker.dll"]