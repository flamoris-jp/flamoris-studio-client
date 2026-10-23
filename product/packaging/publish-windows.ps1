param([string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$out = Join-Path $repo 'artifacts/windows/FLAMORIS-StudioClient-win-x64'
$stage = Join-Path $repo ('artifacts/windows/publish-' + [guid]::NewGuid().ToString('N'))
try {
    dotnet publish (Join-Path $repo 'product/StudioClient.App/StudioClient.App.csproj') -c $Configuration -r win-x64 --self-contained true -o $stage -p:PublishSingleFile=false -p:UseSharedCompilation=false
    if ($LASTEXITCODE -ne 0) { throw 'StudioClient publish failed.' }
    Copy-Item (Join-Path $PSScriptRoot 'setup-audio.ps1') $stage -Force
    Copy-Item (Join-Path $repo 'LICENSE') $stage -Force
    Copy-Item (Join-Path $repo 'docs/windows-user-guide.md') (Join-Path $stage 'README.md') -Force
    $zip = "$out.zip"
    if (Test-Path $zip) { Remove-Item $zip }
    # Archive only this fresh build, never user documents/audio-env saved beside an older EXE.
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip
    New-Item -ItemType Directory -Path $out -Force | Out-Null
    Copy-Item (Join-Path $stage '*') $out -Recurse -Force
}
finally { if (Test-Path $stage) { Remove-Item $stage -Recurse -Force } }
Write-Host "Built: $out"
Write-Host 'First run: ./setup-audio.ps1 (Python + NumPy); FFmpeg and FFprobe must be on PATH.'
