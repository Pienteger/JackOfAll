# JackOfAll

A collection of Windows desktop utilities built with WPF and .NET 10.

## Windows releases

Push a stable version tag to create a GitHub release:

```powershell
git tag v1.0.0
git push origin v1.0.0
```

The Windows release workflow publishes `JackOfAll-win-x64.exe` and
`JackOfAll-win-arm64.exe`, SHA-256 checksums, and a ZIP containing WinGet manifests.
Each executable includes .NET and native dependencies, so users need only download
the executable for their architecture. Windows 10 version 2004 or newer is required.
The executables are unsigned.

Release builds use single-file compression, omit debug symbols, and disable
ReadyToRun to reduce download size. WPF does not support trimming or Native AOT;
the workflow keeps trimming disabled. A framework-dependent build could be smaller,
but would require users to install the .NET 10 Desktop Runtime separately.
Bundled files are extracted to the .NET bundle cache on first launch.

To reproduce the release locally with the .NET 10 SDK:

```powershell
./scripts/Build-Release.ps1 -Tag v1.0.0
```

Use a clean checkout; output goes under `artifacts/`. The script rejects an existing
release directory to prevent stale files from entering a release.

## WinGet publication

The release ZIP contains version, installer, and locale manifests for
`Pienteger.JackOfAll`, with the `jackofall` moniker and command alias. It uses
WinGet's portable installer support and hashes the actual release executables.
Because this repository has no license file, the manifest currently says
`Proprietary`; confirm the publisher, description, and license before submitting.

After the first release is public:

1. Extract the WinGet ZIP and run `winget validate` against its version directory.
2. Test both architectures on matching Windows machines, including SQLite features.
3. Submit the included `manifests/p/Pienteger/JackOfAll/<version>/` directory in a
   pull request to [microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs).

After Microsoft accepts and indexes the package, users can run:

```powershell
winget install jackofall
# Exact identifier if the short name matches multiple packages:
winget install --id Pienteger.JackOfAll --exact
```

Generating release manifests does not itself register the package with WinGet.
Submit the generated manifests for each later release as well. See Microsoft's
[manifest submission instructions](https://learn.microsoft.com/en-us/windows/package-manager/package/repository).
