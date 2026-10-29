# Builds the Meloday Music release: clean publish (self-contained x64) and the per-user installer.
# Output: artifacts\final\MelodayMusic-Setup-<version>-x64.exe
# The version comes from Directory.Build.props (the one place it is set) unless one is passed.
param([string]$Version = "")
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if (-not $Version) { $Version = ([xml](Get-Content (Join-Path $root "Directory.Build.props"))).Project.PropertyGroup.Version }
Write-Host "Building Meloday Music $Version"
& (Join-Path $root 'tools/engine/download.ps1')
$publish = [IO.Path]::GetFullPath((Join-Path $root "artifacts\publish\x64"))
$expected = [IO.Path]::GetFullPath((Join-Path $root "artifacts\publish\x64"))
if ($publish -ne $expected -or -not $publish.StartsWith([IO.Path]::GetFullPath($root) + [IO.Path]::DirectorySeparatorChar)) { throw "Publish path is outside the workspace" }

if (Test-Path -LiteralPath $publish) { Remove-Item -LiteralPath $publish -Recurse -Force }
dotnet publish (Join-Path $root "src\MelodayMusic.App") -c Release -p:Platform=x64 -r win-x64 --self-contained true -p:Version=$Version -o $publish
if ($LASTEXITCODE -ne 0) { throw "Publish failed" }
if (-not (Test-Path (Join-Path $publish "MelodayMusic.pri"))) { throw "MelodayMusic.pri missing from publish output" }
if (-not (Test-Path (Join-Path $publish "MelodayMusic.exe"))) { throw "MelodayMusic.exe missing from publish output" }

$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 (ISCC.exe) not found" }

& $iscc "/DAppVersion=$Version" (Join-Path $PSScriptRoot "MelodayMusic.iss")
if ($LASTEXITCODE -ne 0) { throw "Installer build failed" }
$installer = Join-Path $root "artifacts\final\MelodayMusic-Setup-$Version-x64.exe"
if (-not (Test-Path -LiteralPath $installer)) { throw "Installer output missing" }
(Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash | Set-Content -LiteralPath ($installer + ".sha256")
