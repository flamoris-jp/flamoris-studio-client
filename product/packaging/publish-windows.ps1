param([string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$out = Join-Path $repo 'artifacts/windows/FLAMORIS-StudioClient-win-x64'
dotnet publish (Join-Path $repo 'product/StudioClient.App/StudioClient.App.csproj') -c $Configuration -r win-x64 --self-contained true -o $out -p:PublishSingleFile=false -p:UseSharedCompilation=false
if ($LASTEXITCODE -ne 0) { throw 'StudioClient publish failed.' }
Copy-Item (Join-Path $PSScriptRoot 'setup-audio.ps1') $out -Force
Copy-Item (Join-Path $repo 'LICENSE') $out -Force
Copy-Item (Join-Path $repo 'docs/windows-user-guide.md') (Join-Path $out 'README.md') -Force
$zip = "$out.zip"
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path (Join-Path $out '*') -DestinationPath $zip
Write-Host "Built: $out"
Write-Host 'First run: ./setup-audio.ps1 (Python + NumPy); FFmpeg and FFprobe must be on PATH.'
