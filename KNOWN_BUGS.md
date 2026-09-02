# KNOWN BUGS — TPFan-Win

## 1. 30% display bug (reappears after systray override)
- When systray sets manual override, slider sync fails; `FindClosestSnapPoint` does not prevent 30% from leaking back into UI after override reset.
- Status: BROKEN / NOT FIXED

## 2. Manual override broken
- `SelectedSpeedPercent` setter always applies snap; drag to 30% jumps to 100% due to curve snap logic overriding user input.
- Status: BROKEN

## 3. Slider auto-up/down following RPM / speed
- `PollStatusAsync` updates slider on every poll without rate limit; causes flicker and incorrect sync when mode switches.
- Status: BROKEN

## 4. Systray 0% selects all override items
- `UpdateUiState` uses `mi.Text.Contains(...)` instead of exact match; 0% matches all menu items containing "%".
- Status: BROKEN

Notes:
- Fix attempts made (slider sync, exact-match edit) did not resolve.
- Do not treat as resolved.
