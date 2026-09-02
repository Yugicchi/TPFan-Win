# Setup Instructions

## Prerequisites

1. **Visual Studio 2022** with:
   - .NET desktop development workload
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
dotnet build TPFan.GUI
```

### 4. Run Application (Single-binary WPF app)

```bash
cd TPFan.GUI
dotnet run --configuration Release
```

Expected output:
```
TPFan-Win - Starting...
EC fan control: AVAILABLE
Hardware sensors: AVAILABLE
Current temperature: 45°C
Current fan speed: 35%
Current fan RPM: 2850

Detecting fan curve...
Fan curve points detected: 6
  30°C → 0%
  40°C → 20%
  ...
```

### 5. Run Published Single Binary

Download or build the single-binary `TPFan.GUI.exe` (see [Release Process](RELEASE_PROCESS.md) or [Architecture Overview](ARCHITECTURE.md) for publish commands) and run:

```bash
TPFan.GUI.exe
```
**Note:** Administrator privileges are required for EC fan write operations. Without admin, the app starts in read-only mode (sensors still work, EC writes disabled).

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

### Phase 2: Fan Control
- [ ] Research T480 ACPI methods
- [ ] Implement fan override
- [ ] Test stability & safety

### Phase 3: System Tray
- [ ] Add tray icon
- [ ] Implement minimize to tray
- [ ] Add quick presets menu

### Phase 4: Documentation & Packaging
- [ ] Update documentation for single-binary architecture
- [ ] Test self-contained publish
- [ ] Verify EC write requires administrator

## Troubleshooting

### WMI Access Denied
Run Visual Studio/terminal as Administrator.

### Application Not Starting
- Ensure .NET 8.0 SDK is installed
- Check TargetFramework matches installed SDK

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

When that happens `TPFan.GUI` automatically falls back to the ACPI
thermal-zone counter, so you will see a non-zero temperature printed at
startup:

```
LHM CPU temperatures are empty (likely VBS / Hyper-V blocking MSR) —
falling back to ACPI thermal-zone counter for CPU temperature.
Current temperature: 32°C
```

The fan % and RPM however will read `0` because LHM also returns no fan
sensors under VBS and the application does not guess. There are three
workarounds, in order of safety:

1. **Install Lenovo Vantage** (or the standalone "Lenovo System
   Interface Foundation" / "Energy Management" driver). This registers
   the `root\WMI\Lenovo_Fan` (or `IdeaFan`) class which the application
   probes for fan % and RPM. CPU temperature stays on the ACPI
   fallback.
2. **Disable Memory Integrity** under *Windows Security → Device
   Security → Core isolation → Memory integrity*. Reboot required.
   LHM's MSR path is restored, so all sensors come back.
3. **Disable the hypervisor entirely** (only if you do not need
   WSL2/Hyper-V): run `bcdedit /set hypervisorlaunchtype off` from an
   elevated command prompt and reboot. This is the nuclear option.

The fan *write* (override slider) is independent of the monitoring
limitation: as long as the InpOut32 driver is installed and the
application runs elevated, the override still works. See "EC Fan Control
(T480)" below.

## EC Fan Control (ThinkPad T480)

> **Hardware only.** Without the InpOut32 driver and an elevated process, the
> application falls back to read-only mode: WMI temperature / speed / RPM still
> work, but the manual override slider does not move the fan.

The override path is:
```
WPF Slider ── data binding ──► MainViewModel
    │
    │  (in-proc method call — no IPC)
    ▼
T480FanProvider.SetFanSpeedOverrideAsync(percent)
    │
    │  validate + record _isOverrideActive / _overridePercent
    ▼
IFanController  →  EcFanController.SetFanSpeedAsync(percent)
    │
    │  MapPercentToLevel(percent) ── using FanControlOptions.MaxLevel=7
    ▼
InpOut32 P/Invoke (Out32 / Inp32) via inpoutx64.dll + inpoutx64.sys
    │
    │  EC command port 0x66, data port 0x62 (ACPI EC protocol)
    ▼
EC offset 0x2F (level 0..7), 0x31 (engagement: 0x00=auto, 0x40=manual)
    → fan spins at the requested level
```

Read path (temperature/RPM) uses `LibreHardwareMonitorSensorService` (LHM
with WMI/ACPI fallback). It does **not** depend on the InpOut32 driver —
so the application runs in read-only mode when not elevated or when the
driver is absent.

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

### 3. Place the DLL beside the application executable

For development, the DLL is already placed in `TPFan.GUI/native/` and will
be copied to the output directory.

For the published single binary, `inpoutx64.dll` is bundled inside the
exe and extracted to a temporary folder at runtime.

`EcFanController.IsAvailable` returns `false` if the DLL cannot
be found at startup; the application keeps running in read-only mode.

### 4. Run the application as Administrator

```cmd
cd TPFan.GUI
dotnet run --configuration Release
```

Or run the published binary:
```cmd
TPFan.GUI.exe
```

You should see:
```
EC fan control: AVAILABLE
```

If you see `EC fan control: unavailable`, the application still works for
monitoring — only the override slider is a no-op.

### 5. Verify on hardware

With the application running and elevated:

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

# Publish single binary
dotnet publish TPFan.GUI/TPFan.GUI.csproj \
  --configuration Release --runtime win-x64 --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true \
  --output ./publish/tpfan

./publish/tpfan/TPFan.GUI.exe
```