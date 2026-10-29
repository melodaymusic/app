param([string]$Generator = 'Visual Studio 18 2026')
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$cmake = (Get-Command cmake -ErrorAction SilentlyContinue).Source
if (-not $cmake) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    $visualStudio = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    $cmake = Join-Path $visualStudio 'Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe'
}
if (-not (Test-Path -LiteralPath $cmake)) { throw 'CMake and Visual Studio C++ tools are required.' }
$sources = Join-Path $root 'artifacts\projectm-source'
$build = Join-Path $root 'artifacts\projectm-build'
$bridgeBuild = Join-Path $root 'artifacts\projectm-bridge-build'
$native = Join-Path $root 'src\MelodayMusic.App\Native\win-x64'
New-Item -ItemType Directory -Force $sources, $native | Out-Null
$archive = Join-Path $root 'src\MelodayMusic.App\Assets\ProjectM\Source\libprojectM-4.1.8.tar.gz'
if ((Get-FileHash -LiteralPath $archive).Hash -ne 'F72135E8B8E5FD807F908537255B41AC7BA4830309265E830CB756535487239E') { throw 'projectM source checksum mismatch.' }
& tar -xzf $archive -C $sources
if ($LASTEXITCODE -ne 0) { throw 'Could not extract projectM sources.' }
$glewArchive = Join-Path $sources 'glew.zip'
if (-not (Test-Path -LiteralPath $glewArchive)) {
    Invoke-WebRequest 'https://github.com/nigels-com/glew/releases/download/glew-2.2.0/glew-2.2.0-win32.zip' -OutFile $glewArchive
}
if ((Get-FileHash -LiteralPath $glewArchive).Hash -ne 'EA6B14A1C6C968D0034E61FF6CB242CFF2CE0EDE79267A0F2B47B1B0B652C164') { throw 'GLEW checksum mismatch.' }
Expand-Archive -LiteralPath $glewArchive -DestinationPath $sources -Force
$source = Join-Path $sources 'libprojectM-4.1.8'
$glew = Join-Path $sources 'glew-2.2.0'
$glewOptions = @("-DGLEW_INCLUDE_DIR=$glew/include", "-DGLEW_SHARED_LIBRARY_RELEASE=$glew/lib/Release/x64/glew32.lib")
& $cmake -S $source -B $build -G $Generator -A x64 -DENABLE_PLAYLIST=OFF -DENABLE_SYSTEM_PROJECTM_EVAL=OFF -DCMAKE_MSVC_RUNTIME_LIBRARY=MultiThreaded -DCMAKE_POLICY_DEFAULT_CMP0091=NEW @glewOptions
if ($LASTEXITCODE -ne 0) { throw 'projectM configuration failed.' }
& $cmake --build $build --config Release --parallel 8
if ($LASTEXITCODE -ne 0) { throw 'projectM build failed.' }
& $cmake -S $PSScriptRoot -B $bridgeBuild -G $Generator -A x64 "-DPROJECTM_SOURCE=$source" "-DPROJECTM_BUILD=$build" @glewOptions
if ($LASTEXITCODE -ne 0) { throw 'Bridge configuration failed.' }
& $cmake --build $bridgeBuild --config Release --parallel 4
if ($LASTEXITCODE -ne 0) { throw 'Bridge build failed.' }
Copy-Item -LiteralPath "$build\src\libprojectM\Release\projectM-4.dll", "$glew\bin\Release\x64\glew32.dll", "$bridgeBuild\Release\Meloday.ProjectM.dll" -Destination $native -Force
Copy-Item -Path "$native\*.dll" -Destination "$bridgeBuild\Release" -Force
& "$bridgeBuild\Release\projectm-smoke.exe" "$root\src\MelodayMusic.App\Assets\ProjectM\Presets" "$root\artifacts\projectm-frames"
if ($LASTEXITCODE -ne 0) { throw 'projectM rendering checks failed.' }
