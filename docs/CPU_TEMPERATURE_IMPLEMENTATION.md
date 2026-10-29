# CPU temperature implementation

Local implementation only. Do not commit or push until the user has verified it and explicitly asks.

- [x] 1. Analyze plan and assign parent-owned privileged work.
- [x] 2. Publish slice map with exclusive file ownership.
- [x] 3. Wave 0: shared wire protocol, nullable readings, opt-in setting, CPU/GPU strip source.
- [x] 4. Implement and review service, reader/display, and setup controls.
- [x] 5. Review actual diffs after every Luna slice.
- [x] 6. Resolve review findings; parent takes over hard integration failures.
- [x] 7. Verify coverage, build, regression checks, package and document local testing limits.

## Slice map

| Wave | Slice | Files | Owner / model / effort | Review |
| --- | --- | --- | --- | --- |
| 0 | Contracts | `src/Shared/CpuTemperatureProtocol.cs`, `src/StatsBar/Core/Models.cs`, `src/StatsBar/Settings/AppSettings.cs`, `src/StatsBar/StatsBar.csproj` | Parent / current SOL / high; shared design | Complete |
| 1 | Sensor service | `src/StatsBar.SensorService/`, including notices | Parent / current SOL / high; privileged installation and pipe access | Pass; built, actual transport exercised without installing service |
| 1 | Reader and display | `src/StatsBar/Sources/CpuTemperatureReader.cs`, `src/StatsBar/Modules/CpuModule.cs`, `src/StatsBar/Modules/SensorsModule.cs` | Luna / gpt-6-luna / high | Resumed once, then pass; typing and cancellation cleanup corrected |
| 2 | Setup controls | `src/StatsBar/Ui/Settings/SettingsWindow.cs`, `src/StatsBar/Settings/SensorServiceSetup.cs` | Luna / gpt-6-luna / high | Resumed once, then pass; parent added bounded scrolling during integration |
| 3 | App integration | `src/StatsBar/App.xaml.cs`, `src/StatsBar/Sources/MachineProbe.cs`, `src/StatsBar/StatsBar.csproj` after Wave 0 | Parent / current SOL / high; collection lifetime and cross-cutting wiring | Pass |
| 3 | Packaging and documentation | `scripts/package.ps1`, `README.md`, `THIRD_PARTY_NOTICES.md` | Luna / gpt-6-luna / high | Pass; parent corrected exact labels/visibility instructions and added dependency license files |
| 3 | Regression checks | `tests/StatsBar.SensorChecks/` | Luna / gpt-6-luna / high | Resumed once, then pass; parent extended tests to actual service transport |
| 3 | Layout and transport integration | `src/StatsBar/Ui/Cards/ModuleCardWindow.cs`, settings scrolling, service transport, regression checks | Parent / current SOL / high; integration findings | Pass; four gauges now fit, Settings scrolls, unread pipe data is drained with a deadline |

## Plan steps

1. Add a separate .NET Windows service using LibreHardwareMonitorLib 0.9.6 with CPU monitoring only. Sample in the background every two seconds, select a package/die sensor before the hottest core fallback, and publish a bounded, timestamped, read-only named-pipe message. Report missing driver, unsupported CPU, and failures honestly.
2. Install/repair/remove the optional service only through explicit Settings actions and Windows elevation. Copy service files to a protected Program Files directory before registering LocalSystem; reject linked paths and protect file permissions. The taskbar application remains unelevated. Direct users to the official PawnIO installer rather than silently installing a driver.
3. Add an opt-in asynchronous named-pipe reader. Never wait for the service in Capture or on the UI thread. Bound connection/read time and message size, validate timestamps and finite temperatures, clear stale data, cancel polling when disabled, and release resources on shutdown.
4. Display CPU temperature in the CPU details and Sensors details/gauges/chart, using existing Celsius/Fahrenheit formatting. Add a CPU/GPU choice for the Sensors taskbar temperature while preserving GPU as the default. Keep unavailable readings as `--` with a useful status.
5. Add Settings controls for CPU temperature, taskbar source, setup/repair, remove, and the official PawnIO download page. Setup failures and UAC cancellation must be visible and must not silently enable monitoring. Persist opt-in only on Save.
6. Wire the reader into MachineProbe and App lifetime/settings. Include the service in normal builds and self-contained packages, update setup documentation and third-party notices, and add meaningful dependency-light regression checks for selection, validation, formatting/display, and reader cancellation/failure.

## Verification

All six plan steps are implemented. No implementation steps were skipped or blocked. Delegated slices touched only their allowed files. Parent-owned additions are listed above. Concurrent update-checking work in Settings, README, and other files was preserved.

- Release compilation: passed with zero warnings and zero errors.
- Regression executable: 23 checks passed, zero failures. Covers sensor selection, timestamp and numeric validation, default settings, Celsius/Fahrenheit display, unavailable readings, disabled/re-enabled readers, malformed/oversized/truncated/stale messages, stalled-read recovery, cancellation, and actual server delivery/drain deadlines.
- PowerShell package parser and whitespace checks: passed. No repository lint tool is configured.
- Self-contained x64 portable packaging: passed, including the standalone service and license notices. Local output: `artifacts/release/WinStats-portable-x64.zip`.
- Layout QA: rendered bounded Settings and a Sensors card with illustrative readings. Confirmed scrolling and a two-by-two gauge arrangement without horizontal clipping. Preview files are under ignored `.cache/`.

Service installation/removal, driver access, and actual CPU readings still require an interactive elevated setup and the official PawnIO driver. Neither service nor driver was installed during automated checks. Hardware compatibility and idle CPU cost of the privileged reader must be verified on target machines. The per-user uninstaller leaves the optional administrator-installed service alone; remove it in Settings before uninstalling if no longer needed. The portable package includes a second .NET runtime for the standalone service and is approximately 100 MB compressed.

Nothing was committed or pushed.
