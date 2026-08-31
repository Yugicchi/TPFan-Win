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

### CPU temperature reads as `0` even though the fan is spinning

LibreHardwareMonitorLib relies on Intel `RDMSR` (model-specific register
reads) to obtain per-core and package temperatures. On Windows 10/11
machines that ship with **Virtualization-Based Security (VBS)**,
**Memory Integrity** (HVCI), **Hyper-V**, **WSL2**, **Credential Guard**,
or **Defender Application Guard** enabled, the hypervisor traps MSR
reads and LHM reports every CPU temperature sensor as `null`. You can
confirm with:

```powershell
systeminfo | findstr /C:"Virtualization" /C:"hypervisor"
# or
Get-CimInstance Win32_DeviceGuard | Select-Object VirtualizationBasedSecurityStatus
```

When that happens `TPFan.Service` automatically falls back to the ACPI
thermal-zone counter, so you will see a non-zero temperature printed at
startup:

```
LHM CPU temperatures are empty (likely VBS / Hyper-V blocking MSR) —
falling back to ACPI thermal-zone counter for CPU temperature.
Current temperature: 32°C
```

The fan % and RPM however will read `0` because LHM also returns no fan
sensors under VBS and the service does not guess. There are three
workarounds, in order of safety:

1. **Install Lenovo Vantage** (or the standalone "Lenovo System
   Interface Foundation" / "Energy Management" driver). This registers
   the `root\WMI\Lenovo_Fan` (or `IdeaFan`) class which the service
   probes for fan % and RPM. CPU temperature stays on the ACPI
   fallback.
2. **Disable Memory Integrity** under *Windows Security → Device
   Security → Core isolation → Memory integrity*. Reboot required.
   LHM's MSR path is restored, so all sensors come back.
3. **Disable the hypervisor entirely** (only if you do not need
   WSL2/Hyper-V): run `bcdedit /set hypervisorlaunchtype off` from an
   elevated command prompt and reboot. This is the nuclear option.

The fan *write* (override slider) is independent of the monitoring
limitation: as long as the InpOut32 driver is installed and the service
runs elevated, the override still works. See "EC Fan Control (T480)"
below.

## EC Fan Control (ThinkPad T480)

> **Hardware only.** Without the InpOut32 driver and an elevated process, the
> service falls back to read-only mode: WMI temperature / speed / RPM still
> work, but the manual override slider does not move the fan.

The override path is:
```
UWP slider → Named Pipe → FanServicePipeServer.SetFanSpeedOverrideAsync
            → T480FanProvider.SetFanSpeedOverrideAsync
            → EcFanController.SetFanSpeedAsync
            → InpOut32 (inpoutx64.dll + inpoutx64.sys)
            → Embedded Controller port 0x62/0x66
            → ThinkPad EC firmware
            → fan PWM
```

### 1. Obtain the InpOut32 driver

Download the x64 build from <https://www.highrez.co.uk/Downloads/InpOut32/>.
You need:
- `inpoutx64.dll` — user-mode shim (P/Invoke `Inp32` / `Out32`)
- `inpoutx64.sys` — signed kernel driver that actually issues the I/O

### 2. Install the driver (one-time, admin)

From an **Administrator** command prompt, run the bundled `InstallDriver.exe`,
or install the service manually:

```cmd
sc create inpoutx64 type= kernel binPath= "<abs-path>\inpoutx64.sys"
sc start  inpoutx64
```

Confirm with:
```cmd
sc query inpoutx64
```

If the service reports `STOPPED` and `1058` (the driver refuses to start),
you are missing the matching `.sys` for your build. Some Lenovo BIOSes also
block unsigned drivers — `inpoutx64.sys` from the official Highrez zip is
EV-signed and should load without `bcdedit /set testsigning on`.

### 3. Place the DLL beside the service executable

Copy `inpoutx64.dll` into:
```
TPFan.Service/bin/x64/Release/net8.0-windows10.0.19041.0/
```
or, for the self-contained publish from CI, into the `native/` folder before
publishing. `EcFanController.IsAvailable` returns `false` if the DLL cannot
be found at startup; the service keeps running in read-only mode.

### 4. Run the service as Administrator

```cmd
cd TPFan.Service
dotnet run --configuration Release
```

You should see:
```
EC fan control: AVAILABLE
```

If you see `EC fan control: unavailable`, the service still works for
monitoring — only the override slider is a no-op.

### 5. Verify on hardware

With the service running and the UWP app open:

1. Drag the override slider to `80%`.
2. The UI's `SpeedPercent` and `RPM` should jump within a second or two.
3. Install [RWEverything](http://rweverything.com/) and watch EC offset
   `0x2F` while moving the slider — it should change between 0 and 7.
4. Toggle override off, watch EC offset `0x32` accept a `0x00` write and
   the fan return to firmware auto control.

### EC register map (T480)

Defaults are encoded in `FanControlOptions` and can be overridden via
`appsettings.json` (e.g. for older T480 BIOS revisions that expose the fan
at a different offset):

| Register | Default | Purpose |
|----------|---------|---------|
| `0x2F` | WriteRegister  | Fan level 0..7 |
| `0x2F` | ReadRegister   | Current level (mirror) |
| `0x31` | ModeRegister   | `0x00` = auto, `0x40` = manual |
| `0x32` | ResetRegister  | `0x00` releases manual control |

If the offsets differ on your machine, capture them with RWEverything
while running the default T480 fan curve, then update `FanControlOptions`
(or `appsettings.json` once the binding is added) and rebuild.

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
