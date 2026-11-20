[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Tag,
    [string]$Repository = 'Pienteger/JackOfAll'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($Tag -notmatch '^v(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$') {
    throw 'Use a stable version tag such as v1.0.0.'
}
$version = $Tag.Substring(1)
if ($Repository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') {
    throw 'Repository must be owner/name.'
}
$root = Split-Path $PSScriptRoot -Parent
$release = Join-Path $root 'artifacts/release'
if (Test-Path $release) { throw 'artifacts/release already exists. Use a clean checkout for each release.' }
New-Item -ItemType Directory -Path $release -Force | Out-Null
$installers = @()
$checksums = @()
foreach ($architecture in @('x64', 'arm64')) {
    $rid = "win-$architecture"
    $publish = Join-Path $root "artifacts/publish/$rid"
    # WPF cannot be trimmed. Compression and disabling ReadyToRun reduce size
    # without changing globalization or removing localized resources.
    & dotnet publish (Join-Path $root 'JackOfAll.AppUi/JackOfAll.AppUi.csproj') `
        --configuration Release --runtime $rid --self-contained true --output $publish `
        "-p:Version=$version" -p:PublishSingleFile=true -p:PublishTrimmed=false `
        -p:PublishReadyToRun=false -p:EnableCompressionInSingleFile=true `
        -p:IncludeAllContentForSelfExtract=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:DebugType=None -p:DebugSymbols=false -p:GenerateDocumentationFile=false
    if ($LASTEXITCODE -ne 0) { throw "Publish failed for $rid." }
    $files = @(Get-ChildItem $publish -File -Recurse)
    if ($files.Count -ne 1 -or $files[0].Name -ne 'JackOfAll.AppUi.exe') {
        throw "Expected exactly one executable for $rid; found: $($files.FullName -join ', ')"
    }
    $name = "JackOfAll-$rid.exe"
    $target = Join-Path $release $name
    Copy-Item -LiteralPath $files[0].FullName -Destination $target
    $hash = (Get-FileHash $target -Algorithm SHA256).Hash
    $checksums += "$hash  $name"
    $installers += @"
  - Architecture: $architecture
    InstallerUrl: https://github.com/$Repository/releases/download/$Tag/$name
    InstallerSha256: $hash
"@
    Write-Host "$name : $([math]::Round((Get-Item $target).Length / 1MB, 2)) MiB"
}
$checksums | Set-Content (Join-Path $release 'SHA256SUMS.txt') -Encoding utf8
$manifest = Join-Path $root "artifacts/winget/manifests/p/Pienteger/JackOfAll/$version"
New-Item -ItemType Directory -Path $manifest -Force | Out-Null
@"
PackageIdentifier: Pienteger.JackOfAll
PackageVersion: $version
DefaultLocale: en-US
ManifestType: version
ManifestVersion: 1.6.0
"@ | Set-Content (Join-Path $manifest 'Pienteger.JackOfAll.yaml') -Encoding utf8
@"
PackageIdentifier: Pienteger.JackOfAll
PackageVersion: $version
PackageLocale: en-US
Publisher: Pienteger
PackageName: JackOfAll
PackageUrl: https://github.com/$Repository
License: Proprietary
ShortDescription: A collection of Windows desktop utilities.
Moniker: jackofall
ManifestType: defaultLocale
ManifestVersion: 1.6.0
"@ | Set-Content (Join-Path $manifest 'Pienteger.JackOfAll.locale.en-US.yaml') -Encoding utf8
@"
PackageIdentifier: Pienteger.JackOfAll
PackageVersion: $version
InstallerType: portable
MinimumOSVersion: 10.0.19041.0
Commands:
  - jackofall
Installers:
$($installers -join "`n")
ManifestType: installer
ManifestVersion: 1.6.0
"@ | Set-Content (Join-Path $manifest 'Pienteger.JackOfAll.installer.yaml') -Encoding utf8
Compress-Archive -Path (Join-Path $root 'artifacts/winget/manifests') `
    -DestinationPath (Join-Path $release "JackOfAll-winget-$version.zip")
