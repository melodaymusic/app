# Reproducible model restore. This downloads data only; it never executes remote code.
#  - audio.onnx: ME's CLAP music audio model, as published.
#  - text-w8.onnx + tokenizer.json: CLAP's matching text model with its weights stored in 8 bits, made here from the published
#    full model by tools/engine/vocabulary/quantize.py (deterministic; the result is checked against a pinned SHA-256). It lets
#    search read words that aren't in ME's vocabulary. Making it needs Python 3; the pinned packages go in a private venv.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$engine = Join-Path $root 'src/MelodayMusic.App/Assets/Engine'
$cache = Join-Path $PSScriptRoot 'vocabulary/model'
$revision = 'd4d4372c9894b975ec118589b4f4328103796e15'

function Test-Pinned($path, $hash) { (Test-Path -LiteralPath $path) -and (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -eq $hash }

function Get-Pinned($name, $target, $hash) {
    if (Test-Pinned $target $hash) { return }
    $temp = $target + '.download'
    try {
        Invoke-WebRequest -Uri "https://huggingface.co/muzaiten/clap-htsat-base-onnx/resolve/$revision/$name" -OutFile $temp -UseBasicParsing
        if ((Get-FileHash -LiteralPath $temp -Algorithm SHA256).Hash -ne $hash) { throw "ME model integrity check failed: $name" }
        Move-Item -LiteralPath $temp -Destination $target -Force
    } finally { if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Force } }
}

Get-Pinned 'audio.onnx' (Join-Path $engine 'audio.onnx') '9533ED823C150FB13C91ECD7359AA3BFF1AA6DA7B44099A21CCEA10C0ADBA2D6'

$tokenizerHash = '2BB1A22CFBE25B8E5A232B7FC4D7FC5073923B45724A5F813B00811BB6620F66'
$smallHash = '1EF727D2F8DAC2A51A23AF0A9B52230F2CF5C139D6B43603A7A0493592521A81'
$small = Join-Path $engine 'text-w8.onnx'
Get-Pinned 'tokenizer.json' (Join-Path $engine 'tokenizer.json') $tokenizerHash
if (Test-Pinned $small $smallHash) { return }

New-Item -ItemType Directory -Force $cache | Out-Null
$full = Join-Path $cache 'text.onnx'
Get-Pinned 'text.onnx' $full '069E323E309284AE9ACA6587B884AFCD38F943FA1CBA260823F4673B18F09A26'
Get-Pinned 'tokenizer.json' (Join-Path $cache 'tokenizer.json') $tokenizerHash

$venv = Join-Path $cache 'venv'
$python = Join-Path $venv 'Scripts/python.exe'
if (-not (Test-Path -LiteralPath $python)) {
    $system = Get-Command python -ErrorAction SilentlyContinue
    if (-not $system) { throw 'Making the ME text model needs Python 3 (python on PATH).' }
    & $system.Source -m venv $venv
    if ($LASTEXITCODE -ne 0) { throw 'Could not create the ME tooling venv' }
}
& $python -m pip install --quiet --disable-pip-version-check onnxruntime==1.23.2 onnx==1.23.2 tokenizers==0.23.3 numpy==2.5.5 protobuf==7.36.2
if ($LASTEXITCODE -ne 0) { throw 'Could not install the ME tooling packages' }
$temp = $small + '.making'
try {
    & $python -I (Join-Path $PSScriptRoot 'vocabulary/quantize.py') $full (Join-Path $cache 'tokenizer.json') (Join-Path $PSScriptRoot 'vocabulary/vocabulary.json') $temp
    if ($LASTEXITCODE -ne 0) { throw 'Making the ME text model failed' }
    if ((Get-FileHash -LiteralPath $temp -Algorithm SHA256).Hash -ne $smallHash) { throw 'ME text model integrity check failed: text-w8.onnx' }
    Move-Item -LiteralPath $temp -Destination $small -Force
} finally { if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Force } }
