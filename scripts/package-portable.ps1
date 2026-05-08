param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$MozaSdkPath = "D:\MOZA_SDK\MOZA_SDK\SDK_CSharp\x64"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src\MozaStarCitizen.App\MozaStarCitizen.App.csproj"
$publishRoot = Join-Path $root "artifacts\publish"
$stamp = Get-Date -Format "yyyyMMddHHmmss"
$publishDir = Join-Path $publishRoot "MozaStarCitizen-$Runtime-$stamp"
$zipPath = Join-Path $root "artifacts\MozaStarCitizen-$Runtime-portable.zip"

New-Item -ItemType Directory -Path $publishDir | Out-Null

dotnet publish $project `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:EnableCompressionInSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    --output $publishDir

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

if (-not (Test-Path (Join-Path $publishDir "MozaStarCitizen.exe"))) {
    throw "Publish completed, but MozaStarCitizen.exe was not produced."
}

Get-ChildItem $publishDir -Filter "*.pdb" -File | Remove-Item -Force

$mozaSdkDlls = @("MOZA_API_CSharp.dll", "MOZA_API_C.dll", "MOZA_SDK.dll")
if (Test-Path $MozaSdkPath) {
    $missing = @($mozaSdkDlls | Where-Object { -not (Test-Path (Join-Path $MozaSdkPath $_)) })
    if ($missing.Count -eq 0) {
        $driverDir = Join-Path $publishDir "drivers\moza-sdk\x64"
        New-Item -ItemType Directory -Path $driverDir -Force | Out-Null
        foreach ($dll in $mozaSdkDlls) {
            Copy-Item -Path (Join-Path $MozaSdkPath $dll) -Destination $driverDir -Force
        }
        Write-Host "Bundled MOZA SDK runtime from $MozaSdkPath"
    } else {
        Write-Warning "MOZA SDK path exists but is missing: $($missing -join ', ')"
    }
}

function Write-Launcher {
    param(
        [string]$Name,
        [string]$Mode
    )

    $lines = @(
        "@echo off",
        "set MOZA_SC_OUTPUT=$Mode",
        "start """" ""%~dp0MozaStarCitizen.exe"""
    )
    Set-Content -Path (Join-Path $publishDir $Name) -Value $lines -Encoding ASCII
}

Write-Launcher "Run-Auto.cmd" ""
Write-Launcher "Run-DirectInput.cmd" "DirectInput"
Write-Launcher "Run-MozaSdk.cmd" "MozaSdk"
Write-Launcher "Run-Preview.cmd" "Preview"

if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath -Force
Write-Host "Portable build written to $zipPath"
