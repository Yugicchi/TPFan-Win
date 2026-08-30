# Setup Instructions

## Prerequisites

1. **Visual Studio 2022** with:
   - .NET desktop development workload
   - Windows App SDK (for UWP)
   - Windows 10/11 SDK

2. **.NET 8.0 SDK**
   ```bash
   # Verify installation
   dotnet --version
   ```

3. **ThinkPad T480** (target hardware)

## Project Setup

### 1. Clone & Initialize Git

```bash
git init
git add .
git commit -m "Initial commit: ThinkPad T480 Fan Control skeleton"
```

### 2. Restore Dependencies

```bash
cd TPFan-Win
dotnet restore
```

### 3. Build Projects

```bash
# Build all
dotnet build

# Or build individually
dotnet build TPFan.Shared
dotnet build TPFan.Service
dotnet build TPFan.UWP
```

### 4. Run Service (Console App)

```bash
cd TPFan.Service
dotnet run
```

Expected output:
```
TPFan-Win Service - Starting...
Current temperature: 45°C
Current fan speed: 35%
Current fan RPM: 2850

Detecting fan curve...
Fan curve points detected: 6
  30°C → 0%
  40°C → 20%
  ...
```

### 5. Run UWP App

Open `TPFan.sln` in Visual Studio:
1. Set `TPFan.UWP` as startup project
2. Select `x64` platform
3. Press F5 to run

## Git Commands for Version Control

```bash
# Initialize git (if not done)
git init

# Add all files
git add .

# Initial commit
git commit -m "feat: initial project skeleton for T480 fan control"

# Check status
git status

# View commit history
git log --oneline

# Create feature branch
git checkout -b feature/fan-curve-detection

# After changes
git add .
git commit -m "feat: implement WMI fan reading"

# Merge back
git checkout main
git merge feature/fan-curve-detection
```

## Development Workflow

### Phase 1: Core Functionality
- [ ] Test WMI fan reading on actual T480
- [ ] Verify temperature query works
- [ ] Detect actual fan curve from system
- [ ] Test slider snapping

### Phase 2: Service Integration
- [ ] Implement named pipe IPC
- [ ] Test UWP ↔ Service communication
- [ ] Handle service not running gracefully

### Phase 3: Fan Control
- [ ] Research T480 ACPI methods
- [ ] Implement fan override
- [ ] Test stability & safety

### Phase 4: System Tray
- [ ] Add tray icon
- [ ] Implement minimize to tray
- [ ] Add quick presets menu

### Phase 5: MSIX Packaging
- [ ] Configure package manifest
- [ ] Test sideloading
- [ ] Create installer

## Troubleshooting

### WMI Access Denied
Run Visual Studio/terminal as Administrator

### UWP Build Errors
- Ensure Windows App SDK is installed
- Check TargetFramework matches installed SDK

### Service Not Starting
Check if port/pipe name conflicts with other instances

## Useful Commands

```bash
# Clean build
dotnet clean
dotnet build

# Watch for changes
dotnet watch run

# Run tests (when added)
dotnet test

# Pack as NuGet (if needed)
dotnet pack
```
