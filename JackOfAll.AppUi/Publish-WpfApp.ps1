<#
.SYNOPSIS
    Interactive publish + code-sign script for a WPF app (win-x64).

.NOTES
    Signing removes the "unknown publisher" warning ONLY if you use a real
    OV/EV code signing certificate from a trusted CA. A self-signed cert
    will still trigger SmartScreen for anyone who downloads the app,
    because Windows doesn't trust the issuer. This script supports both,
    but self-signed is for internal testing only.
#>

$ErrorActionPreference = "Stop"

function Ask($prompt, $default) {
    if ($default) {
        $val = Read-Host "$prompt [$default]"
        if ([string]::IsNullOrWhiteSpace($val)) { return $default }
        return $val
    } else {
        return Read-Host $prompt
    }
}

function AskYesNo($prompt, $defaultYes = $true) {
    $suffix = if ($defaultYes) { "Y/n" } else { "y/N" }
    $val = Read-Host "$prompt ($suffix)"
    if ([string]::IsNullOrWhiteSpace($val)) { return $defaultYes }
    return $val.ToUpper().StartsWith("Y")
}

Write-Host "=== WPF Publish & Sign ===" -ForegroundColor Cyan

# --- Project ---
$csproj = Ask "Path to .csproj file"
if (-not (Test-Path $csproj)) { throw "Project file not found: $csproj" }

$outDir = Ask "Output folder" ".\publish"

# --- Publish mode ---
$selfContained = AskYesNo "Self-contained (no .NET runtime needed on target machine)?" $true
$trim = $false
if ($selfContained) {
    $trim = AskYesNo "Enable trimming (smaller size, but riskier for WPF - test the app after!)" $false
}
$singleFile = AskYesNo "Bundle into a single .exe?" $true

# --- Signing ---
Write-Host ""
Write-Host "Signing options:" -ForegroundColor Yellow
Write-Host "  1) I have a .pfx certificate file"
Write-Host "  2) I have a cert already installed (sign by thumbprint)"
Write-Host "  3) Generate a self-signed cert (TESTING ONLY - will still show SmartScreen warning to others)"
Write-Host "  4) Skip signing"
$signChoice = Ask "Choose 1-4" "4"

$pfxPath = $null
$pfxPassword = $null
$thumbprint = $null
$selfSigned = $false

switch ($signChoice) {
    "1" {
        $pfxPath = Ask "Path to .pfx file"
        if (-not (Test-Path $pfxPath)) { throw "PFX not found: $pfxPath" }
        $securePwd = Read-Host "PFX password" -AsSecureString
        $pfxPassword = [Runtime.InteropServices.Marshal]::PtrToStringAuto(
            [Runtime.InteropServices.Marshal]::SecureStringToBSTR($securePwd))
    }
    "2" {
        $thumbprint = Ask "Certificate thumbprint (from Cert:\CurrentUser\My)"
    }
    "3" {
        $selfSigned = $true
        $certSubject = Ask "Subject name for self-signed cert" "CN=MyDevCert"
    }
    default {
        Write-Host "Skipping signing." -ForegroundColor DarkYellow
    }
}

# --- Build publish args ---
$rid = "win-x64"
$args = @(
    "publish", "`"$csproj`"",
    "-c", "Release",
    "-r", $rid,
    "-o", "`"$outDir`"",
    "--self-contained", $selfContained.ToString().ToLower(),
    "-p:DebugType=none",
    "-p:DebugSymbols=false",
    "-p:GenerateDocumentationFile=false",
    "-p:InvariantGlobalization=true",
    "-p:UseSystemResourceKeys=true",
    "-p:SatelliteResourceLanguages=en"
)

if ($singleFile) {
    $args += @(
        "-p:PublishSingleFile=true",
        "-p:EnableCompressionInSingleFile=true",
        "-p:IncludeNativeLibrariesForSelfExtract=true"
    )
}

if ($trim) {
    $args += @(
        "-p:PublishTrimmed=true",
        "-p:TrimMode=full"
    )
}

Write-Host ""
Write-Host "Running: dotnet $($args -join ' ')" -ForegroundColor Cyan
& dotnet @args
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

# --- Find the built exe ---
$exe = Get-ChildItem -Path $outDir -Filter "*.exe" | Where-Object { $_.Name -notlike "*.vshost.exe" } | Select-Object -First 1
if (-not $exe) { throw "Could not find published .exe in $outDir" }
Write-Host "Published: $($exe.FullName)" -ForegroundColor Green

# --- Signing ---
if ($signChoice -in @("1","2","3")) {

    $signtool = Get-ChildItem -Path "C:\Program Files (x86)\Windows Kits\10\bin" -Recurse -Filter "signtool.exe" -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -like "*x64*" } | Select-Object -First 1

    if (-not $signtool) {
        throw "signtool.exe not found. Install the Windows SDK (Windows Kits) to get it."
    }

    if ($selfSigned) {
        Write-Host "Creating self-signed certificate ($certSubject)..." -ForegroundColor Yellow
        $cert = New-SelfSignedCertificate -Type CodeSigning -Subject $certSubject `
            -CertStoreLocation "Cert:\CurrentUser\My" -KeyExportPolicy Exportable `
            -KeyUsage DigitalSignature -NotAfter (Get-Date).AddYears(3)
        $thumbprint = $cert.Thumbprint

        # Optional: trust it locally so *your* machine won't flag it (does nothing for other people)
        $rootStore = New-Object System.Security.Cryptography.X509Certificates.X509Store("Root","CurrentUser")
        $rootStore.Open("ReadWrite")
        $rootStore.Add($cert)
        $rootStore.Close()
        Write-Host "Self-signed cert created and trusted locally only." -ForegroundColor DarkYellow
    }

    $timestampUrl = "http://timestamp.digicert.com"

    if ($pfxPath) {
        & $signtool.FullName sign /f "$pfxPath" /p "$pfxPassword" /fd SHA256 /tr $timestampUrl /td SHA256 "$($exe.FullName)"
    } else {
        & $signtool.FullName sign /sha1 "$thumbprint" /fd SHA256 /tr $timestampUrl /td SHA256 "$($exe.FullName)"
    }

    if ($LASTEXITCODE -ne 0) { throw "Signing failed." }
    Write-Host "Signed successfully." -ForegroundColor Green

    & $signtool.FullName verify /pa "$($exe.FullName)"
}

Write-Host ""
Write-Host "Done. Output at: $($exe.FullName)" -ForegroundColor Cyan

if ($selfSigned) {
    Write-Host ""
    Write-Host "REMINDER: This cert is self-signed. Anyone else who runs this exe" -ForegroundColor Red
    Write-Host "will still see a SmartScreen 'Windows protected your PC' warning," -ForegroundColor Red
    Write-Host "because their machine has no reason to trust your cert." -ForegroundColor Red
}
