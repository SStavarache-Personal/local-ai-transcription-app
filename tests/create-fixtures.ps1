$ErrorActionPreference = 'Stop'
$fixtureRoot = Join-Path $PSScriptRoot 'fixtures'
New-Item -ItemType Directory -Path $fixtureRoot -Force | Out-Null
$ttsScript = Join-Path $PSScriptRoot 'speech-fixture.ps1'
# System.Speech uses the Windows Framework voice engine only to create owned test audio.
& "$env:WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -File $ttsScript -OutputFolder $fixtureRoot
if ($LASTEXITCODE -ne 0) { throw 'Speech fixture generation failed' }
if (-not (Test-Path -LiteralPath (Join-Path $fixtureRoot 'french.wav'))) {
    $devPython = Join-Path (Split-Path -Parent $PSScriptRoot) '.venv\Scripts\python.exe'
    if (Test-Path -LiteralPath $devPython) {
        & $devPython -m pip install 'espeakng-loader==0.2.4'
        & $devPython (Join-Path $PSScriptRoot 'french-fixture.py')
        if ($LASTEXITCODE -ne 0) { throw 'French fixture generation failed' }
    } else { throw 'A French test voice is missing. Create a Python 3.13 .venv and install espeakng-loader==0.2.4, then rerun tests.' }
}
$ffmpeg = Join-Path (Split-Path -Parent $PSScriptRoot) 'dist\LocalTranscriber\runtime\ffmpeg\ffmpeg.exe'
& $ffmpeg -nostdin -v error -i (Join-Path $fixtureRoot 'english.wav') -y (Join-Path $fixtureRoot 'english.mp3')
& $ffmpeg -nostdin -v error -f lavfi -i 'color=c=black:s=320x180:r=10' -i (Join-Path $fixtureRoot 'english.wav') -shortest -c:v mpeg4 -c:a aac -y (Join-Path $fixtureRoot 'english.mp4')
& $ffmpeg -nostdin -v error -i (Join-Path $fixtureRoot 'english.mp4') -c copy -y (Join-Path $fixtureRoot 'english.mkv')
if ($LASTEXITCODE -ne 0) { throw 'Media fixture conversion failed' }
& $ffmpeg -nostdin -v error -i (Join-Path $fixtureRoot 'english.wav') -f lavfi -i 'anoisesrc=color=pink:amplitude=0.025:sample_rate=22050:seed=42' -filter_complex '[0:a][1:a]amix=inputs=2:duration=first:normalize=0' -c:a pcm_s16le -y (Join-Path $fixtureRoot 'noisy-english.wav')
if ($LASTEXITCODE -ne 0) { throw 'Noisy fixture generation failed' }
