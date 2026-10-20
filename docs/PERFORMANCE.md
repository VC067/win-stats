# CPU and memory measurements

These are short observations from one working Windows PC, not a performance guarantee or an isolated benchmark. Other development and preview work was running on the machine during measurement.

## Observed results — 13 October 2026

| Final self-contained build / state | Mean CPU | Mean private memory | Mean working set | Samples |
| --- | ---: | ---: | ---: | ---: |
| Default strip, no detail panel visible | 0.173% | 35.4 MiB | 48.9 MiB | 30 |
| CPU panel confirmed visible | 0.408% | 98.6 MiB | 116.0 MiB | 30 |

CPU percentages use total logical CPU capacity: process CPU time divided by elapsed wall time and 12 logical processors. A task monitor showing a single-core-equivalent percentage will report a different number. MiB means 1,048,576 bytes.

Test host: Windows 11 build 26200, x64, 12 logical processors. Bundled .NET 10.0.11, Release build, application version 0.1.0, with the selected Pulse tray icon. Earlier exploratory readings did not verify panel visibility and are excluded from these results.

The public v0.1.0 packages published on 20 October 2026 bundle .NET 10.0.13. The observations above were recorded earlier with .NET 10.0.11; they are not a new measurement of those published packages.

## Method

The run uses the default CPU/RAM/GPU taskbar strip and one-second system sampling. The diagnostic session warms up for 15 seconds, records 30 seconds with the strip idle, opens the real CPU detail panel, warms up for another five seconds, and records 30 seconds with the panel open. Diagnostic mode keeps the panel visible when focus changes and records the active panel in every sample. All 30 idle samples had no panel visible; all 30 detail samples confirmed the CPU panel. Interrupted samples are excluded, and the script rejects a phase with fewer than 20 verified samples. Monitoring overhead is included. The session does not save preferences or change startup registration.

The existing app performs a one-time working-set trim after 30 seconds. Working set can therefore look smaller without an equivalent reduction in committed private memory. Both figures are reported; private memory is the more useful comparison for this test. Opening a WPF detail panel and collecting process information increases resource use.

Reproduce after building a package, with any other Win Stats instance closed:

```powershell
./scripts/benchmark.ps1
```

The script writes raw readings to ignored `artifacts/performance.json`. It temporarily shows the taskbar strip and CPU panel, then exits. Leave its settings and panel unchanged during the run. An alternate executable/output can be supplied using `-Executable` and `-Output`.

## Limits

This does not test a clean machine without .NET installed, multiple GPU vendors, all display configurations, long-duration stability, or resource use while every possible panel is open. Rerun on representative hardware before making a numeric marketing claim. A self-contained package bundles the runtime for installation convenience; its download size is separate from its running memory use.
