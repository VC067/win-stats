# Release checklist

Complete these steps before the first public release.

- [x] Add the MIT license for original code, website and artwork, retaining required third-party notices.
- [ ] Clone the release commit into a clean checkout and build it.
- [ ] Smoke-test installer and portable ZIP on a clean Windows 11 x64 system without .NET installed.
- [ ] Check startup opt-in, upgrade, and uninstall behavior.
- [ ] Confirm the default configuration works offline; redact identifying details from screenshots and media.
- [ ] Measure CPU and memory footprint and document the results or limitations in `docs/PERFORMANCE.md`.
- [ ] Replace or augment renderer media with a real desktop/taskbar capture before launch, if available.
- [ ] Confirm sensor limitations and unavailable-value behavior on supported hardware.
- [ ] Set the repository description, website, and topics: `windows11`, `system-monitor`, `cpu`, `ram`, `taskbar`, `dotnet`, `wpf`.
- [ ] Enable private vulnerability reporting in repository settings.
- [ ] Upload `assets/brand/social-preview.png` as the repository social preview after the license is selected.
- [ ] Create and push a version tag (for example `v0.1.0`) on the release commit; the manual workflow's selected ref must resolve to that same tag commit.
- [ ] Run the Draft release workflow, review the draft and all three release assets, then publish it.
- [ ] Verify the GitHub Pages deployment from `main`. Future website changes deploy automatically; manual dispatch is also available.
- [ ] Verify the public website, releases page, download links, checksums, and Ko-fi link.
