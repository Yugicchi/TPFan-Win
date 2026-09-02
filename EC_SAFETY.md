# EC Safety — ThinkPad T480 Fan Control

## Incident Timeline (2026-09-02)

| Time | Event | Source |
|------|-------|--------|
| 18:00:43 – 18:03:40 | WiFi noisy (irrelevant) | Netwtw06 |
| **18:03:30–40** | **ACPI Warning ID 15**: EC returned data when none was requested | ACPI |
| 18:04:35 | Services failed (irrelevant — boot noise) | SCM |
| **18:12:36** | **ACPI Error ID 13**: EC did not respond within timeout | ACPI |
| **18:23:08** | **Unexpected shutdown** | EventLog 6008 |
| 18:24:00 | Hyper-V warning (irrelevant) | Hyper-V |
| 18:24:05 | WUDFRd driver fail (irrelevant) | Kernel-PnP |
| 18:24:13–27 | Services fail on boot (after shutdown) | SCM |
| **18:25:18** | **CPU thermal throttling** (71 s) after reboot | Kernel-Processor-Power |

## Root Cause Analysis

### ACPI Warning ID 15 — 18:03:30
> "The embedded controller (EC) returned data when none was requested."

**Interpretation**: Our app sent writes to EC ports while Windows' own ACPI driver
was mid-cycle. Both sides accessed the EC simultaneously — a classic bus contention.
The EC responded with stale data, confusing the ACPI driver.

### ACPI Error ID 13 — 18:12:36
> "The embedded controller (EC) did not respond within the specified timeout period.
> This may indicate that there is an error in the EC hardware or firmware or that
> the BIOS is accessing the EC incorrectly."

**Interpretation**: ~9 minutes after the first warning, the EC became fully
unresponsive. The culprit is most likely an invalid register write that put the
EC into a bad state (e.g. writing an out-of-range byte, or sending commands too
fast for the EC microcontroller to process). Once the EC is hung, every subsequent
read/write from both the OS and our app stalls at the IBF/OBF polling stage.

### Unexpected Shutdown — 18:23:08
**Interpretation**: ~11 minutes after the EC hang, the laptop lost power.
The BIOS watchdog timer fired because the EC stopped sending the thermal/heartbeat
signals it normally reports to the southbridge. Without those signals, the firmware
assumes the system is overheating or has a critical hardware fault → immediate power-off.

### CPU Thermal Throttling — 18:25:18
**Interpretation**: After reboot, the CPU was still hot from the forced shutdown.
The firmware limited CPU clock speed until temperatures normalised.

---

## Mitigations Applied

### [1] Global Named Mutex — `Global\\TPFan-Win.EcAccess`
```
AcquireAndRun() wraps every EcRead/EcWrite call.
Timeout: 2 s — if the mutex is held by a crashed thread (AbandonedMutexException),
we claim ownership and continue.  If we can't acquire it, the operation is SKIPPED
rather than blocked indefinitely.
```
**Why**: Serialises all EC access within our process. This doesn't prevent
Windows ACPI from accessing the EC concurrently (that driver doesn't take our
mutex), but it prevents burst writes from multiple threads inside our app
from overwhelming the EC.

### [2] Inter-Byte Delay — 20 ms after every Out32/Inp32
```
EcRead:  after CmdPort write  → Sleep(20)
          after DataPort write → Sleep(20)
          after Inp32 read     → Sleep(20)
EcWrite: after CmdPort write  → Sleep(20)
          after DataPort write → Sleep(20) ×2 (offset + value)
WaitForStatus: unchanged (spin-wait is already <1 µs per iteration)
```
**Why**: The EC microcontroller on the T480 processes each byte sequentially.
Sending bytes back-to-back without a settling period can cause the EC to
drop or misinterpret a byte, leading to an invalid command → EC hang.
20 ms is a conservative middle ground (Gemini suggested 10–50 ms).

### [3] Strict IBF/OBF Handshake — unchanged, already correct
`WaitForStatus()` already polls the status port before every I/O port access.
This is the correct protocol and must never be bypassed.

---

## On-Disk Safeguards

| Location | What it does |
|----------|-------------|
| `EcFanController.Dispose()` | Calls `ResetToAutoAsync()` with 2 s hard timeout before exit |
| `T480FanProvider.Dispose()` | Same — 2 s timeout on `ResetToAutoAsync()` |
| `AcquireAndRun()` | Skips operation if mutex cannot be acquired in 2 s |
| `App.OnExit` | Disposes provider → EC controller → tray → sensors |

---

## Safe Testing Protocol

### Before testing
1. Save all work — close VS Code, browsers, documents
2. Reboot first if fan behaved erratically in the last session
3. Run as Administrator — required for EC I/O port access
4. Check `gui.log` for previous errors

### During testing
1. Monitor `gui.log` — watch for EC errors or timeouts
2. Test modes separately — Auto first, then Manual
3. **Wait 2–3 seconds** between Auto/Manual toggles
4. Watch fan behaviour — if fan stops or ramps to 100% and stays, kill app immediately
5. Do NOT rapidly drag the slider

### After testing
1. Check fan is responsive — should ramp up/down with temperature
2. If shutdown occurs — reboot, let BIOS reset EC, test again

---

## Code Status

| Item | Status |
|------|--------|
| `Global\\TPFan-Win.EcAccess` mutex | ✅ Applied in `EcFanController` |
| Inter-byte 20 ms delay | ✅ Applied in `EcRead` / `EcWrite` |
| Strict IBF/OBF handshake | ✅ Already correct, not changed |
| Mutex timeout (2 s) | ✅ `AcquireAndRun()` |
| Mutex skip on timeout | ✅ Returns fallback, skips EC op |
| `AcquireAndRun()` for all ops | ✅ All 4 public methods |
| Dispose `EcFanController` | ✅ `App.OnExit` now disposes it |
| Updated 2026-09-02 | ✅ |

## Next Steps
1. Test with `gui.log` open — watch for `[EC] AcquireAndRun: mutex timeout`
2. If timeout appears repeatedly → reduce polling frequency in `MainViewModel`
3. Consider adding EC state health check on startup (read a known register, validate value)
4. Consider making the inter-byte delay configurable via `FanControlOptions`
