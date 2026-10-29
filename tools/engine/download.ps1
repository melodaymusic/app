# Reproducible model restore. This downloads data only; it never executes remote code.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$target = Join-Path $root 'src/MelodayMusic.App/Assets/Engine/audio.onnx'
$expected = '9533ED823C150FB13C91ECD7359AA3BFF1AA6DA7B44099A21CCEA10C0ADBA2D6'
if ((Test-Path -LiteralPath $target) -and (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -eq $expected) { return }
$temp = $target + '.download'
try {
    Invoke-WebRequest -Uri 'https://huggingface.co/muzaiten/clap-htsat-base-onnx/resolve/d4d4372c9894b975ec118589b4f4328103796e15/audio.onnx' -OutFile $temp -UseBasicParsing
    if ((Get-FileHash -LiteralPath $temp -Algorithm SHA256).Hash -ne $expected) { throw 'ME model integrity check failed' }
    Move-Item -LiteralPath $temp -Destination $target -Force
} finally { if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Force } }
