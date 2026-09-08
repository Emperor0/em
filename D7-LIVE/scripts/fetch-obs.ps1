$ErrorActionPreference = 'Stop'

$Version = '32.2.2'
$ExpectedSha256 = '4d6e40e3ab155f56b30de517380566a206d74b63cdf5ad49aa596924768f97e1'
$Url = "https://github.com/obsproject/obs-studio/releases/download/$Version/OBS-Studio-$Version-Windows-x64.zip"
$DestinationDir = Join-Path $PSScriptRoot '..\src-tauri\resources\obs'
$Destination = Join-Path $DestinationDir 'obs.zip'

New-Item -ItemType Directory -Path $DestinationDir -Force | Out-Null

if (Test-Path $Destination) {
    $current = (Get-FileHash $Destination -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($current -eq $ExpectedSha256) {
        Write-Host "OBS Studio $Version runtime already verified."
        exit 0
    }
    Remove-Item $Destination -Force
}

Write-Host "Downloading verified OBS Studio $Version runtime..."
Invoke-WebRequest -Uri $Url -OutFile $Destination -UseBasicParsing
$actual = (Get-FileHash $Destination -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actual -ne $ExpectedSha256) {
    Remove-Item $Destination -Force -ErrorAction SilentlyContinue
    throw "OBS runtime SHA-256 mismatch. Expected $ExpectedSha256 but got $actual"
}

Write-Host "Verified: $actual"
Write-Host "Runtime ready: $Destination"
