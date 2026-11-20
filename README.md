[![Windows release](https://github.com/Pienteger/JackOfAll/actions/workflows/release.yml/badge.svg)](https://github.com/Pienteger/JackOfAll/actions/workflows/release.yml)

# JackOfAll

A collection of Windows desktop utilities built with WPF and .NET 10.

## Windows releases

Push a stable version tag to create a GitHub release:

```powershell
git tag v1.0.1
git push origin v1.0.1
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

The first version must be accepted into `microsoft/winget-pkgs` before automated
updates can run. The initial submission is
[PR #484751](https://github.com/microsoft/winget-pkgs/pull/484751).

Later tagged releases automatically submit WinGet update pull requests using
[winget-releaser](https://github.com/vedantmgoyal9/winget-releaser), after the
executables are published. The WinGet job runs in the same workflow because
releases created with `GITHUB_TOKEN` do not trigger another release workflow.

One-time setup:

1. Create a [classic GitHub personal access token](https://github.com/settings/tokens/new)
   for **MahmudX**, with `public_repo` and `workflow` scopes.
2. Add it as the **WINGET_TOKEN** repository secret under **Settings → Secrets and
   variables → Actions → New repository secret** in
   [repository settings](https://github.com/Pienteger/JackOfAll/settings/secrets/actions).

The action uses the existing `MahmudX/winget-pkgs` fork. If you use a different
account, fork `microsoft/winget-pkgs` there and set the repository Actions variable
`WINGET_FORK_USER` to that account. Microsoft still validates and reviews each
submitted update. A WinGet submission failure leaves the GitHub release available.

### Publish an automatic WinGet update

After the initial WinGet package is accepted and the one-time setup is complete:

1. Commit and push the changes you want to release to `master`.
2. Tag that commit with a new stable version and push the tag:

   ```powershell
   git tag v1.0.1
   git push origin v1.0.1
   ```

   Replace `1.0.1` with the next unused version. The tag sets the executable and
   WinGet package versions; no project-file version edit is needed.
3. Open [Windows release runs](https://github.com/Pienteger/JackOfAll/actions/workflows/release.yml).
   The `release` job publishes both executables, then `winget` opens an update PR
   in `microsoft/winget-pkgs` using `WINGET_TOKEN`.
4. After Microsoft merges and indexes the update, users can upgrade with:

   ```powershell
   winget upgrade --id Pienteger.JackOfAll --exact
   ```

If only the `winget` job fails, fix the reported issue (for example, an expired
token), then choose **Re-run failed jobs** on that workflow run. Do not recreate
the release or reuse its version tag for different binaries.
