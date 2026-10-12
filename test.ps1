param([switch]$Smoke, [switch]$Extended, [switch]$Ui, [switch]$Dictation, [string]$Asr = 'parakeet-v3-int8,whisper-small,whisper-medium,whisper-large-v3-turbo', [string]$Llm = 'lfm25-1.2b-instruct,lfm25-2.6b', [string]$ReleaseName = 'LocalTranscriber')
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
if ($ReleaseName -notmatch '^[A-Za-z0-9-]+$') { throw 'ReleaseName must be a folder name containing letters, digits or hyphens' }
$releaseRoot = Join-Path $PSScriptRoot ('dist\' + $ReleaseName)
$releaseExe = Join-Path $releaseRoot 'LocalTranscriber.exe'
if (-not (Test-Path -LiteralPath $releaseExe)) { throw 'Run build.ps1 first' }
$env:LOCALTRANSCRIBER_DATA = Join-Path $PSScriptRoot 'test-output\data'
$env:LOCALTRANSCRIBER_REPORT = Join-Path $PSScriptRoot 'test-output\test-results.json'
New-Item -ItemType Directory -Path 'test-output' -Force | Out-Null
try {
    & (Join-Path $releaseRoot 'runtime\python\python.exe') workers\asr_worker.py --self-test
    if ($LASTEXITCODE -ne 0) { throw 'Worker self-check failed' }
    $arguments = @('--self-test')
    if ($Smoke) { & 'tests\create-fixtures.ps1'; $arguments += @('--smoke','--fixtures',(Join-Path $PSScriptRoot 'tests\fixtures'),'--asr',$Asr,'--llm',$Llm) }
    if ($Extended) { if (-not $Smoke) { throw 'Use -Extended with -Smoke' }; $arguments += '--extended' }
    if ($Ui -or $Dictation) {
        if ($Smoke -or $Extended) { throw 'Run -Ui separately, after -Smoke has installed Parakeet and LFM2.5 Instruct.' }
        $uiOutput = Join-Path $PSScriptRoot 'test-output\ui'; New-Item -ItemType Directory -Path $uiOutput -Force | Out-Null
        $env:LOCALTRANSCRIBER_DATA = Join-Path $PSScriptRoot 'test-output\ui-data'
        $env:LOCALTRANSCRIBER_REPORT = Join-Path $uiOutput 'ui-results.json'
        $arguments = @('--ui-check','--output',$uiOutput,'--fixtures',(Join-Path $PSScriptRoot 'tests\fixtures'),'--models',(Join-Path $PSScriptRoot 'test-output\data\integration-index\models'))
        if ($Dictation) {
            $uiOutput = Join-Path $PSScriptRoot 'test-output\dictation'; New-Item -ItemType Directory -Path $uiOutput -Force | Out-Null
            $env:LOCALTRANSCRIBER_DATA = Join-Path $PSScriptRoot 'test-output\dictation-data'
            $env:LOCALTRANSCRIBER_REPORT = Join-Path $uiOutput 'dictation-results.json'
            $arguments = @('--ui-check','--dictation-check','--output',$uiOutput,'--fixtures',(Join-Path $PSScriptRoot 'tests\fixtures'),'--models',(Join-Path $PSScriptRoot 'test-output\data\integration-index\models'))
        }
    }
    $processInfo = [Diagnostics.ProcessStartInfo]::new($releaseExe)
    $processInfo.UseShellExecute = $false
    $processInfo.CreateNoWindow = $true
    if ($Ui -or $Dictation) { $processInfo.Environment['PATH'] = "$env:WINDIR\System32"; $processInfo.Environment['DOTNET_ROOT'] = 'C:\not-installed'; $processInfo.Environment['PYTHONHOME'] = 'C:\not-installed' }
    foreach ($argument in $arguments) { $processInfo.ArgumentList.Add($argument) }
    $testProcess = [Diagnostics.Process]::Start($processInfo)
    $testProcess.Id | Set-Content -LiteralPath 'test-output\last-test-pid.txt'
    $testProcess.WaitForExit()
    if ($testProcess.ExitCode -ne 0) { Get-Content -LiteralPath $env:LOCALTRANSCRIBER_REPORT; throw 'App checks failed' }
    Get-Content -LiteralPath $env:LOCALTRANSCRIBER_REPORT
} finally { Remove-Item Env:\LOCALTRANSCRIBER_DATA -ErrorAction SilentlyContinue; Remove-Item Env:\LOCALTRANSCRIBER_REPORT -ErrorAction SilentlyContinue }
