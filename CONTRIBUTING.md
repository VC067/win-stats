# Contributing

Thanks for helping improve Win Stats. Bug reports, focused fixes, and practical feature ideas are welcome.

## Build

On Windows with the .NET 10 SDK installed, run:

```powershell
dotnet restore src/StatsBar/StatsBar.csproj
dotnet build src/StatsBar/StatsBar.csproj -c Release
```

GUI smoke tests are manual: launch the built app on Windows and check the taskbar display and the settings or module cards relevant to your change. There is no automated test project.

## Changes

Keep changes focused and describe the user-visible behavior and how you checked it. For pull requests, include screenshots when a visual change is easier to understand that way. Do not include secrets, personal data, or generated build and diagnostic files.

Packages can be produced with `scripts/package.ps1`.

## Brand and media assets

The selected logo is Pulse. Brand artwork is original SVG; app and website icon exports are checked in, so normal application builds need no image tooling.

To regenerate the brand assets, install Node.js and Sharp (`npm install --no-save --no-package-lock sharp`), then run `node scripts/build-brand.mjs --concept pulse`. This also updates the Windows icon and website assets. For sample UI media, run a built `WinStats.exe --media-export website/assets/media` on Windows. The images use illustrative data, not personal desktop captures.
