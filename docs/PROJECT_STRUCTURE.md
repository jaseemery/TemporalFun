# TemporalWorker Project Structure

This document describes the organized file structure of the TemporalWorker application.

## 📁 **Directory Structure**

```
TemporalWorker/
├── 📁 src/                          # Source code organized by functionality
│   ├── 📁 Services/                 # Background services and workers
│   │   └── HotReloadWorkerService.cs
│   ├── 📁 Managers/                 # Business logic managers
│   │   └── HotReloadManager.cs
│   ├── 📁 Watchers/                 # File system watchers
│   │   └── PackageWatcher.cs
│   └── 📁 Loaders/                  # Activity and assembly loaders
│       └── ActivityLoader.cs
├── 📁 Activities/                   # Sample Temporal activities
│   ├── DatabaseActivity.cs
│   └── EmailActivity.cs
├── 📁 config/                       # Configuration files
│   ├── NuGet.Config
│   ├── NuGet.Config.build
│   └── NuGet.Config.runtime
├── 📁 docs/                         # Documentation
│   ├── README.md
│   ├── PRODUCTION_RECOMMENDATIONS.md
│   └── PROJECT_STRUCTURE.md (this file)
├── 📁 scripts/                      # Utility and test scripts
│   ├── setup-artifactory.sh
│   ├── test-graceful-restart.sh
│   └── simulate-artifactory-update.sh
├── 📁 examples/                     # Example implementations
│   ├── 📁 microservices-example/
│   ├── 📁 sample-packages/
│   ├── 📁 test-deletion/
│   └── 📁 TemporalWorker/
├── 📁 logs/                         # Log files (generated)
├── 📁 tests/                        # Test files (for future use)
├── 📁 bin/                          # Build output (generated)
├── 📁 obj/                          # Build intermediate files (generated)
├── Program.cs                       # Application entry point
├── TemporalWorker.csproj           # Project file
├── Dockerfile                       # Docker configuration
└── docker-compose.yml             # Docker Compose configuration
```

## 🏗️ **Architecture Overview**

### **Core Components**

#### **Services (`src/Services/`)**
- **HotReloadWorkerService**: Main background service that manages Temporal worker lifecycle
- Handles graceful restarts and hot reloading of activities
- Implements proper resource disposal and error handling

#### **Managers (`src/Managers/`)**
- **HotReloadManager**: Orchestrates the hot reload process
- Scans for assembly changes and discovers new activities
- Manages the reload lifecycle and event notifications

#### **Watchers (`src/Watchers/`)**
- **PackageWatcher**: Monitors file system for package changes
- Watches NuGet package directories and application output
- Triggers reload events when changes are detected

#### **Loaders (`src/Loaders/`)**
- **ActivityLoader**: Loads and manages Temporal activities
- Handles both static and dynamic activity loading
- Coordinates with hot reload components

### **Configuration (`config/`)**
- **NuGet.Config**: Standard NuGet package sources
- **NuGet.Config.build**: Build-time package configuration
- **NuGet.Config.runtime**: Runtime package configuration

### **Scripts (`scripts/`)**
- **test-graceful-restart.sh**: Comprehensive testing script for graceful restart functionality
- **simulate-artifactory-update.sh**: Simulates real Artifactory package changes
- **setup-artifactory.sh**: Artifactory configuration setup

### **Examples (`examples/`)**
- Contains sample implementations and reference code
- Microservices examples and package templates
- Test cases and experimental features

## 🔧 **Key Features**

### **Graceful Restart System**
1. **Graceful Shutdown**: Workers complete current tasks before stopping
2. **Resource Management**: Proper disposal of connections and resources
3. **Error Handling**: Comprehensive error handling with timeouts
4. **Status Monitoring**: Real-time logging of restart operations

### **Hot Reload Capability**
1. **File System Monitoring**: Automatic detection of package changes
2. **Dynamic Loading**: Runtime loading of new activity assemblies
3. **Activity Discovery**: Automatic discovery of Temporal activities
4. **Event-Driven**: Event-based architecture for reload notifications

### **Production Features**
1. **Configurable**: Environment-based configuration
2. **Docker Support**: Full containerization support
3. **Logging**: Comprehensive structured logging
4. **Monitoring**: Health checks and status reporting

## 🚀 **Getting Started**

### **Build and Run**
```bash
# Build the project
dotnet build

# Run the application
dotnet run

# Run with specific configuration
TEMPORAL_SERVER=my-server:7233 TASK_QUEUE=my-queue dotnet run
```

### **Testing**
```bash
# Test graceful restart functionality
./scripts/test-graceful-restart.sh

# Simulate Artifactory package changes
./scripts/simulate-artifactory-update.sh --auto
```

### **Docker**
```bash
# Build Docker image
docker build -t temporal-worker .

# Run with Docker Compose
docker-compose up
```

## 📋 **Development Guidelines**

### **Namespace Organization**
- `TemporalWorkerApp`: Root namespace
- `TemporalWorkerApp.Services`: Background services
- `TemporalWorkerApp.Managers`: Business logic managers
- `TemporalWorkerApp.Watchers`: File system watchers
- `TemporalWorkerApp.Loaders`: Activity and assembly loaders

### **Adding New Components**
1. Place files in appropriate `src/` subdirectory
2. Use correct namespace for the component type
3. Update project documentation
4. Add appropriate unit tests (when test framework is added)

### **Configuration**
- Use environment variables for runtime configuration
- Store static configuration in `config/` directory
- Document configuration options in relevant docs

This organized structure provides clear separation of concerns, improved maintainability, and better scalability for the TemporalWorker application.