# Third-party notices

Win Stats’ original code, website and artwork are licensed under the MIT License in [LICENSE](LICENSE). The components below retain their own terms.

## Website animation

The website includes GSAP 3.15.0 and ScrollTrigger 3.15.0 from the [GSAP npm package](https://www.npmjs.com/package/gsap/v/3.15.0), by GreenSock / Webflow. Their original copyright and license headers are retained in `website/assets/vendor/`.

- [GSAP standard license](https://gsap.com/standard-license/)
- [Source repository](https://github.com/greensock/GSAP)

These files are third-party dependencies; selecting a license for the application or website does not relicense GSAP.

## Bundled .NET runtime

Self-contained Windows packages include Microsoft .NET and Windows Desktop runtime files. The packaging script copies the notices from the exact runtime package versions used by the build into:

- `notices/dotnet/Microsoft.NETCore.App-LICENSE.txt`
- `notices/dotnet/Microsoft.NETCore.App-THIRD-PARTY-NOTICES.txt`
- `notices/windowsdesktop/Microsoft.WindowsDesktop.App-LICENSE.txt`

See the runtime [source and license](https://github.com/dotnet/runtime) and the [WPF source](https://github.com/dotnet/wpf). The complete bundled notices are included in the ZIP and installed application.

## Windows and GPU components

Segoe UI / Segoe Fluent Icons fonts, Windows system libraries, and NVIDIA's `nvml.dll` are loaded from the user's Windows installation or GPU driver. They are not redistributed in this repository or copied into the application packages.

The application icon, brand source artwork, and sample UI media were created for Win Stats. Sample media is produced by the application's renderer with illustrative values, rather than captured from a user's personal desktop.
