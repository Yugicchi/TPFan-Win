# TPFan-Win v0.3.0 — Testing Guide

## Build & Deployment

### Release Build
```powershell
dotnet publish TPFan.GUI/TPFan.GUI.csproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  --output ./publish/tpfan
```

This creates `./publish/tpfan/TPFan.GUI.exe` — a single 200MB+ self-extracting binary containing:
- .NET 8 runtime
- inpoutx64.dll (EC fan control driver interface)
- All dependencies

### Test Environment
- **Hardware**: Lenovo ThinkPad T480 (or equivalent EC-capable laptop)
- **OS**: Windows 11 (or Windows 10 21H2+)
- **User Privileges**: Administrator (required for EC writes)

---

## Test Scenarios

### Scenario 1: First Launch (Admin Privileges)

**Step 1.1**: Run as Administrator
```powershell
# Right-click TPFan.GUI.exe → Run as Administrator
# OR
Start-Process -FilePath ".\publish\tpfan\TPFan.GUI.exe" -Verb RunAs
```

**Expected**: Window opens with:
- Header: "🌀 TPFan-Win"
- Status card showing:
  - Temperature badge (circle) with current CPU temp in °C (e.g., "45°")
  - "RPM: [value]" — should be > 0 if fan is spinning
  - "Speed: [0-100]%" — current fan speed as %
  - "Mode: Auto" — in green (auto mode)
- Fan Control card:
  - Toggle switch labeled "A" (Auto mode)
  - Label shows "Current: Auto" in green
  - No slider visible (only appears in Manual mode)
- Fan Curve canvas — shows a line graph (blue curve)
- Bottom: "🟢 Hardware connected" in green

**Verify**:
- [ ] Temperature updates every 2 seconds (watch the value change)
- [ ] RPM > 0 (not stuck at 0)
- [ ] Speed % updates (not blank, not stuck at 0)
- [ ] Mode shows "Auto" in green
- [ ] No error dialogs

---

### Scenario 2: Toggle to Manual Mode

**Step 2.1**: Click the "A" toggle switch
- The switch should smoothly animate to "M" (Manual)
- Background color changes from gray to blue
- Slider appears below the switch

**Expected state after toggle**:
- Toggle shows "M" and is blue
- Slider appears with:
  - Large text in center showing current % (e.g., "45%")
  - Horizontal slider bar
  - Labels: "0%" | "50%" | "100%"
- Status card: "Mode: Manual" in orange

**Verify**:
- [ ] Toggle animates smoothly
- [ ] Slider appears (not just becomes active, but was hidden before)
- [ ] Current % displayed in large text
- [ ] Mode label changes to "Manual" in orange

---

### Scenario 3: Slider Snaps to Fan Curve

**Step 3.1**: Move slider slowly
- Drag slider left/right
- Watch the large % text change

**Expected**: Slider should "snap" to discrete points, not be continuously smooth.
- Typical snap points for T480: 0%, 20%, 30%, 40%, 50%, 60%, 70%, 80%, 100%
- Slider jumps between these, doesn't slide smoothly
- Each jump corresponds to a fan curve threshold temperature

**Verify**:
- [ ] Slider snaps to discrete points (not smooth continuous)
- [ ] Points match fan curve (roughly every 10°C threshold)
- [ ] Slider does NOT jump randomly (snaps are consistent)

---

### Scenario 4: Fan Hardware Responds

**Step 4.1**: Set slider to 100% (max fan)
- Move slider all the way right to 100%

**Expected**: 
- Fan spins audibly faster (loud whoosh from vents)
- "RPM: [value]" in status card increases
- "Speed: 100%" in status card
- Status: "Mode: Manual" in orange

**Verify** (using one or more methods):
- [ ] Hear fan spin up (loudest noise from laptop)
- [ ] RPM value increases in real-time
- [ ] Speed % shows 100%
- [ ] Use third-party tool (e.g., RWEverything) to confirm EC register 0x2F = 0x07 (max level)

**Step 4.2**: Set slider to 0% (min fan)
- Move slider all the way left to 0%

**Expected**:
- Fan slows to minimal (nearly silent)
- RPM drops
- Speed % shows 0-5%

**Verify**:
- [ ] Fan noise decreases significantly
- [ ] RPM drops
- [ ] EC register 0x2F = 0x00 (min level)

---

### Scenario 5: Real-time Graph Update

**Step 5.1**: Set slider to a mid-range value (e.g., 50%)
- Keep window visible for 30 seconds
- Watch the fan curve canvas (the graph area)

**Expected**:
- Orange dot on the curve moves as CPU temp fluctuates
- Dot position = current temp (x-axis) vs. current speed (y-axis)
- Dot should move even if slider doesn't change (as temp varies naturally)
- Every 2 seconds: graph redraws to reflect current state

**Verify**:
- [ ] Orange dot on curve is visible
- [ ] Dot moves when temp changes (not stuck)
- [ ] Graph canvas redraws (watch it update)

---

### Scenario 6: Toggle Back to Auto Mode

**Step 6.1**: Click the "M" toggle to switch back to Auto
- Toggle animates back to "A"
- Slider disappears
- EC writes stop (fan controlled by firmware again)

**Expected**:
- Toggle shows "A" and is gray
- Slider hidden (not just disabled — not visible)
- Mode shows "Auto" in green
- RPM/Speed % continue to update from sensors
- Fan speed may change as firmware adjusts for current temp

**Verify**:
- [ ] Toggle animates smoothly back to "A"
- [ ] Slider hidden (was visible, now gone)
- [ ] Mode shows "Auto" in green
- [ ] Fan behavior returns to firmware control

---

### Scenario 7: Run Without Admin Privileges (Graceful Degradation)

**Step 7.1**: Run TPFan.GUI.exe as regular user (not admin)
- Do NOT right-click "Run as Administrator"

**Expected**:
- Window still opens
- Temperature/RPM/Speed % still update from sensors
- Toggle still exists but does nothing (disabled/greyed out)
- Slider still hidden (always hidden without admin)
- Status: "⚠️ Hardware unavailable" in red (or similar)
- Console output: "EC fan control: unavailable — inpoutx64.dll not found or driver not installed"

**Verify**:
- [ ] App doesn't crash
- [ ] Displays last-known readings
- [ ] Toggle is visible but non-functional
- [ ] No error dialogs

---

### Scenario 8: Close and Reopen

**Step 8.1**: Close window while in Manual mode (slider active)
**Step 8.2**: Immediately reopen as Administrator

**Expected**:
- Fan returns to Auto mode (Dispose() called ResetToAutoAsync)
- Toggle opens in "A" state
- Slider hidden

**Verify**:
- [ ] Fan resets to firmware control on app close
- [ ] App opens cleanly
- [ ] No stale fan overrides lingering

---

## Diagnostic Output

### Check Console Logs

If running from PowerShell, console output includes:
```
[App] Initializing fan provider...
[Provider] GetFanStatusAsync: temp=45°C, rpm=2100, speed=30%, override_active=false
[Poll] Curve detected: 6 points (30-80°C range)
[EC] EC fan control: AVAILABLE (inpoutx64.dll found and driver loaded)
```

### Check GUI Logs

Logs are written to:
```
%LOCALAPPDATA%\TPFan-Win\gui.log
```

On typical Windows 11:
```
C:\Users\[USERNAME]\AppData\Local\TPFan-Win\gui.log
```

Look for:
- `EC fan control: AVAILABLE` — confirms driver is present
- `SetFanSpeedOverrideAsync(50%)` — confirms slider writes
- `Override reset to auto` — confirms toggle back to Auto worked

---

## Known Limitations

### 1. VBS / Hyper-V Blocks MSR
If Windows has Virtual Machine Platform (Hyper-V) enabled, LibreHardwareMonitor cannot read MSR temperature sensors. In this case:
- **Temperature from LHM**: 0°C (unavailable)
- **Fallback**: ACPI WMI thermal zone (usually works, returns ~ambient temp)
- **Result**: May see 30°C steady, even under load

**Workaround**: Disable Hyper-V if you need accurate CPU temp. Or accept that accurate temp requires native Linux.

### 2. inpoutx64.sys Driver Not Installed
If `inpoutx64.dll` exists but `inpoutx64.sys` is not installed/loaded:
- **EC writes fail silently**
- **Slider moves but fan doesn't respond**
- **RPM still shows (EC tachometer reads)**
- **Speed % shows fallback (estimated from override value)**

**Workaround**: The dll + driver are bundled in the single binary. If still failing:
1. Check Windows Device Manager for "Unknown devices" or errors
2. Run `devmgmt.msc` and look for port I/O device
3. Manually install WinIO driver (from Windows DDK or third-party InpOut repository)

### 3. Fan Curve Not Detected
If fan curve detection times out:
- **Status shows**: "Hardware unavailable" in red
- **Slider ranges**: 0-100% (no snap points)
- **Expected behavior**: Slider still works, just less refined

---

## Rollback / Troubleshooting

### Fan Stuck at High Speed After Crash
If the app crashes while in Manual mode, the fan may stay at the last override level.

**Fix**:
1. Restart the app as Administrator
2. Set toggle to "A" (Auto) — this calls `ResetToAutoAsync()`
3. Close app — this also resets on exit

### Fan Unresponsive
If slider moves but fan doesn't change:
1. Check console: is `SetFanSpeedOverrideAsync` being called?
2. Confirm admin privileges
3. Confirm inpoutx64.sys is loaded (Device Manager)
4. Try manual reset with `RWEverything`:
   - Write 0x80 to EC port 0x66 (value to port 0x62) — switches to BIOS auto

### Temperature Always 0°C
1. Check if Hyper-V is enabled (VBS blocks MSR)
2. Check console for fallback message: "Using ACPI WMI thermal zone"
3. Try LibreHardwareMonitor directly to see if it detects sensors

---

## Success Criteria

The following must all be true for v0.3.0 to be considered working:

- [ ] **RPM displays correctly**: Not 0, updates every 2 sec
- [ ] **Speed % displays correctly**: Not blank, updates every 2 sec
- [ ] **Slider snaps**: Discrete points (not continuous smooth)
- [ ] **Graph updates**: Orange dot moves on curve canvas, redraws every 2 sec
- [ ] **Auto/Manual toggle works**: Smooth animation, slider appears/disappears
- [ ] **Fan hardware responds**: Speed up to 100%, slow to 0%, audible change
- [ ] **Graceful degradation**: Works without admin (no crash, displays last reading)
- [ ] **No stale override**: Fan resets to Auto on app close

---

## Next Steps

### If All Tests Pass
- Release v0.3.0 on GitHub (tag, release notes)
- Update README with single-binary distribution method

### If Tests Fail
1. Check `gui.log` for error messages
2. Run with `--verbose` flag (if implemented)
3. Open GitHub issue with:
   - Console output (first 50 lines)
   - gui.log contents
   - Hardware model (ThinkPad model number)
   - Windows version
   - Repro steps

---

## Appendix: Manual EC Testing with RWEverything

For advanced testing, you can inspect EC registers directly:

1. Download RWEverything from http://www.rweverything.com/
2. Run as Administrator
3. **Read current fan level**:
   - Port I/O → Port: `0x66`, Click "Read" → Value should be `0x62`
   - This reads EC register 0x2F (fan level)

4. **Write manual fan level**:
   - Port: `0x66`, Value: `0x62`, Click "Write"
   - Port: `0x66`, Value: `0x07`, Click "Write" (set fan to max)
   - Wait 2 seconds
   - Read back to verify: should now show `0x07`

5. **Read tachometer RPM**:
   - Port: `0x66`, Value: `0x84`, Click "Write" (select tachometer low byte)
   - Port: `0x62`, Click "Read" → Note value (e.g., `0x12`)
   - Port: `0x66`, Value: `0x85`, Click "Write" (select tachometer high byte)
   - Port: `0x62`, Click "Read" → Note value (e.g., `0x20`)
   - RPM = (high << 8) | low = (0x20 << 8) | 0x12 = 0x2012 = 8210 RPM

This confirms the EC hardware is responding and our driver integration is correct.
