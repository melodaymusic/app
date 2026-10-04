# Builds the Meloday Music release: clean publish (self-contained x64) and the per-user installer.
# Output: artifacts\final\MelodayMusic-Setup-<version>-x64.exe
# The version comes from Directory.Build.props (the one place it is set) unless one is passed.
param([string]$Version = "")
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if (-not $Version) { $Version = ([xml](Get-Content (Join-Path $root "Directory.Build.props"))).Project.PropertyGroup.Version }
Write-Host "Building Meloday Music $Version"
$publish = Join-Path $root "artifacts\publish\x64"

if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
dotnet publish (Join-Path $root "src\HumeMusic.App") -c Release -p:Platform=x64 -r win-x64 --self-contained true -p:Version=$Version -o $publish
if ($LASTEXITCODE -ne 0) { throw "Publish failed" }
if (-not (Test-Path (Join-Path $publish "MelodayMusic.pri"))) { throw "MelodayMusic.pri missing from publish output" }

$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 (ISCC.exe) not found" }

& $iscc "/DAppVersion=$Version" (Join-Path $PSScriptRoot "HumeMusic.iss")
if ($LASTEXITCODE -ne 0) { throw "Installer build failed" }
