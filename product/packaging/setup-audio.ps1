param([string]$Python = 'python')
$ErrorActionPreference = 'Stop'
& $Python -c 'import sys; assert sys.version_info >= (3,11), "Python 3.11+ required"'
if ($LASTEXITCODE -ne 0) { throw 'Install Python 3.11+ and put it on PATH, or pass -Python <python.exe>.' }
if (-not (Get-Command ffmpeg -ErrorAction SilentlyContinue) -or -not (Get-Command ffprobe -ErrorAction SilentlyContinue)) { throw 'FFmpeg and FFprobe must be on PATH.' }
$environment = Join-Path $PSScriptRoot 'audio-env'
if (-not (Test-Path (Join-Path $environment 'Scripts/python.exe'))) {
    & $Python -m venv $environment
    if ($LASTEXITCODE -ne 0) { throw 'Could not create the audio environment.' }
}
$audioPython = Join-Path $environment 'Scripts/python.exe'
& $audioPython -m pip install -r (Join-Path $PSScriptRoot 'audio-worker/requirements.txt')
if ($LASTEXITCODE -ne 0) { throw 'NumPy installation failed.' }
& $audioPython -I -c 'import numpy; print("Audio environment ready:", numpy.__version__)'
if ($LASTEXITCODE -ne 0) { throw 'Audio environment validation failed.' }
Write-Host 'Ready. Start StudioClient.exe.'
