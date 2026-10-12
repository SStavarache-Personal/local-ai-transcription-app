$ErrorActionPreference = 'Stop'
$env:LOCALTRANSCRIBER_DATA = 'C:\Evidence\data'
$env:LOCALTRANSCRIBER_REPORT = 'C:\Evidence\smoke.json'
$env:PATH = "$env:WINDIR\System32"
$env:DOTNET_ROOT = 'C:\not-installed'
$env:PYTHONHOME = 'C:\not-installed'
$indexFolder = 'C:\Evidence\data\integration-index'
New-Item -ItemType Directory -Path $indexFolder -Force | Out-Null
@{schemaVersion=1;modelRoot='C:\Models';cacheRoot='C:\Evidence\cache';theme='System';threads=8} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $indexFolder 'settings.json')
$smoke = Start-Process -FilePath 'C:\App\LocalTranscriber.exe' -ArgumentList @('--self-test','--smoke','--fixtures','C:\Harness\fixtures','--asr','parakeet-v3-int8,whisper-small,whisper-medium,whisper-large-v3-turbo','--llm','lfm25-1.2b-instruct,lfm25-2.6b') -Wait -PassThru -WindowStyle Hidden
if ($smoke.ExitCode -ne 0) { throw 'Offline clean-machine smoke failed; see Evidence/smoke.json' }
$env:LOCALTRANSCRIBER_DATA = 'C:\Evidence\ui-data'
$env:LOCALTRANSCRIBER_REPORT = 'C:\Evidence\ui.json'
$uiFolder = 'C:\Evidence\ui'; New-Item -ItemType Directory -Path $uiFolder -Force | Out-Null
$ui = Start-Process -FilePath 'C:\App\LocalTranscriber.exe' -ArgumentList @('--ui-check','--output',$uiFolder,'--fixtures','C:\Harness\fixtures','--models','C:\Models') -Wait -PassThru -WindowStyle Hidden
if ($ui.ExitCode -ne 0) { throw 'Clean-machine UI check failed; see Evidence/ui.json' }
'Passed actual offline/CPU packaged checks in Windows Sandbox.' | Set-Content -LiteralPath 'C:\Evidence\PASSED.txt'
