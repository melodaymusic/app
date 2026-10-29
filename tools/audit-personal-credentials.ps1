param([string]$PublishDirectory = 'artifacts/publish/x64', [string]$CredentialPath = (Join-Path $env:LOCALAPPDATA 'MelodayMusic/lastfm.bin'))
$ErrorActionPreference = 'Stop'
# Read the existing local connection into memory only. Never print or save its credentials.
$credentialMatchesList = [System.Collections.Generic.List[string]]::new()
$values = @()
if (Test-Path -LiteralPath $credentialPath) {
    Add-Type -AssemblyName System.Security
    $entropy = [System.Text.Encoding]::UTF8.GetBytes('MelodayMusic.Secret.LastFm.v1')
    $plain = [System.Security.Cryptography.ProtectedData]::Unprotect([System.IO.File]::ReadAllBytes($credentialPath), $entropy, [System.Security.Cryptography.DataProtectionScope]::CurrentUser)
    $account = [System.Text.Encoding]::UTF8.GetString($plain) | ConvertFrom-Json
    $values = @($account.ApiKey, $account.Secret, $account.SessionKey) | Where-Object { $_ -and $_.Length -ge 16 }
}
$sourceFiles = Get-ChildItem -LiteralPath 'src','installer' -Recurse -File | Where-Object { $_.FullName -notmatch '[\\/](obj|bin)[\\/]' -and $_.Extension -in '.cs','.xaml','.csproj','.json','.config','.iss','.ps1' }
$publishedFiles = Get-ChildItem -LiteralPath $PublishDirectory -Recurse -File | Where-Object { $_.Name -in 'MelodayMusic.exe','MelodayMusic.dll','MelodayMusic.Core.dll','MelodayMusic.pri' -or $_.Extension -in '.json','.config','.ps1','.txt','.xml' }
foreach ($file in @($sourceFiles) + @($publishedFiles)) {
    $bytes = [System.IO.File]::ReadAllBytes($file.FullName)
    $utf8 = [System.Text.Encoding]::UTF8.GetString($bytes)
    $utf16 = [System.Text.Encoding]::Unicode.GetString($bytes)
    foreach ($value in $values) {
        if ($utf8.Contains($value) -or $utf16.Contains($value)) { $credentialMatchesList.Add($file.FullName); break }
    }
}
$privateFiles = @(Get-ChildItem -LiteralPath $PublishDirectory -Recurse -File | Where-Object { $_.Name -in 'lastfm.bin','listenbrainz.bin','listenbrainz-pending.bin','credentials.bin','settings.json','lastfm-pending.json' })
[pscustomobject]@{ LocalConnectionPresent = (Test-Path -LiteralPath $credentialPath); LocalCredentialsChecked = $values.Count; SourceFilesChecked = @($sourceFiles).Count; PublishedFilesChecked = @($publishedFiles).Count; CredentialMatches = $credentialMatchesList.Count; PersonalDataFilesPackaged = $privateFiles.Count }
if ($credentialMatchesList.Count -gt 0 -or $privateFiles.Count -gt 0) { throw 'Personal credential audit failed. Values have not been printed.' }
