# Release checklist

Status of the v0.1.0 launch and remaining manual validation. Unchecked items are follow-up checks, not claims of completed testing.

- [x] Add the MIT license for original code, website and artwork, retaining required third-party notices.
- [x] Build the tagged release commit in a clean GitHub Actions checkout.
- [ ] Smoke-test installer and portable ZIP on a clean Windows 11 x64 system without .NET installed.
- [ ] Check startup opt-in, upgrade, and uninstall behavior.
- [ ] Confirm the default configuration works offline; redact identifying details from screenshots and media.
- [x] Measure CPU and memory footprint and document the results and limitations in `docs/PERFORMANCE.md`.
- [ ] Optionally augment renderer media with a real desktop/taskbar capture.
- [ ] Confirm sensor limitations and unavailable-value behavior on supported hardware.
- [x] Set the repository description, website, and topics: `windows11`, `system-monitor`, `cpu`, `ram`, `taskbar`, `dotnet`, `wpf`.
- [ ] Optionally enable private vulnerability reporting in repository settings.
- [ ] Optionally upload `assets/brand/social-preview.png` as the repository social preview.
- [x] Create and push v0.1.0 on the release commit; the manual workflow's selected ref resolves to the same tag commit.
- [x] Run the Draft release workflow, verify the installer and ZIP downloads and their checksums, then publish v0.1.0.
- [x] Verify the GitHub Pages deployment from `main`. Future website changes deploy automatically; manual dispatch is also available.
- [x] Verify the public website, releases page, download links and checksums preserved in release notes; retain the supplied Ko-fi link.

Published: [repository](https://github.com/VC067/win-stats), [website](https://vc067.github.io/win-stats/), [v0.1.0 release](https://github.com/VC067/win-stats/releases/tag/v0.1.0).
