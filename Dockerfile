# Use the official .NET 9 runtime as base image
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app

# Create non-root user for security
RUN groupadd -r temporal && useradd -r -g temporal temporal

# Use the .NET 9 SDK for building
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy csproj file and restore dependencies
COPY ["TemporalWorker.csproj", "."]
RUN dotnet restore "TemporalWorker.csproj"

# Copy all source files
COPY . .

# Build the application
RUN dotnet build "TemporalWorker.csproj" -c Release -o /app/build

# Publish the application
FROM build AS publish
RUN dotnet publish "TemporalWorker.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Final stage - runtime image
FROM base AS final
WORKDIR /app

# Install curl for healthchecks and netcat for debugging
RUN apt-get update && apt-get install -y curl netcat-traditional && rm -rf /var/lib/apt/lists/*

# Copy the published application
COPY --from=publish /app/publish .

# Set permissions for application directory
RUN chown -R temporal:temporal /app

# Set environment variables for containerized environment
ENV TEMPORAL_SERVER=temporal:7233
ENV TASK_QUEUE=default

# Switch to non-root user
USER temporal

# Set the entry point
ENTRYPOINT ["dotnet", "TemporalWorker.dll"]