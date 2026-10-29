# Rebuilds src/MelodayMusic.App/Assets/Engine/descriptors.json from vocabulary.json with the CLAP text encoder that matches
# ME's audio model (same checkpoint, same pinned conversion). The text model is downloaded here only, verified by SHA-256,
# and never ships with the app. Downloads data only; it never executes remote code.
# After a rebuild, update SoundVocabulary.AssetHash in src/MelodayMusic.Core/Audio/SoundVocabulary.cs to the printed SHA-256.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
$cache = Join-Path $PSScriptRoot 'model'
$revision = 'd4d4372c9894b975ec118589b4f4328103796e15'
$files = @{
    'text.onnx'      = '069E323E309284AE9ACA6587B884AFCD38F943FA1CBA260823F4673B18F09A26'
    'tokenizer.json' = '2BB1A22CFBE25B8E5A232B7FC4D7FC5073923B45724A5F813B00811BB6620F66'
}
New-Item -ItemType Directory -Force $cache | Out-Null
foreach ($name in $files.Keys) {
    $target = Join-Path $cache $name
    if ((Test-Path -LiteralPath $target) -and (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -eq $files[$name]) { continue }
    $temp = $target + '.download'
    try {
        Invoke-WebRequest -Uri "https://huggingface.co/muzaiten/clap-htsat-base-onnx/resolve/$revision/$name" -OutFile $temp -UseBasicParsing
        if ((Get-FileHash -LiteralPath $temp -Algorithm SHA256).Hash -ne $files[$name]) { throw "CLAP $name integrity check failed" }
        Move-Item -LiteralPath $temp -Destination $target -Force
    } finally { if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Force } }
}

$out = Join-Path $root 'src/MelodayMusic.App/Assets/Engine/descriptors.json'
$tokens = Join-Path $cache 'tokens.json'
dotnet run --project (Join-Path $PSScriptRoot 'Vocabulary.csproj') -c Release -- `
    (Join-Path $cache 'text.onnx') (Join-Path $cache 'tokenizer.json') (Join-Path $PSScriptRoot 'vocabulary.json') $out $tokens
if ($LASTEXITCODE -ne 0) { throw 'Vocabulary build failed' }

# Optional parity check of the C# tokenizer against Hugging Face tokenizers (pip install tokenizers).
$python = Get-Command python -ErrorAction SilentlyContinue
if ($python) {
    & $python.Source -I (Join-Path $PSScriptRoot 'check_tokens.py') (Join-Path $cache 'tokenizer.json') $tokens
    if ($LASTEXITCODE -ne 0) { throw 'Tokenizer parity check failed' }
}
