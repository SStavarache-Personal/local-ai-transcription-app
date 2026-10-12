param([string]$Models = (Join-Path (Split-Path -Parent $PSScriptRoot) 'test-output\data\integration-index\models'), [ValidateSet(16384,32768)][int]$MemoryInMB = 16384)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$sandboxOutput = Join-Path $repoRoot 'test-output\sandbox'
New-Item -ItemType Directory -Path $sandboxOutput -Force | Out-Null
$modelPath = (Resolve-Path -LiteralPath $Models).Path
$appPath = (Resolve-Path -LiteralPath (Join-Path $repoRoot 'dist\LocalTranscriber')).Path
$harnessPath = (Resolve-Path -LiteralPath $PSScriptRoot).Path
function XmlText([string]$value) { [Security.SecurityElement]::Escape($value) }
$config = @"
<Configuration>
 <VGpu>Disable</VGpu><Networking>Disable</Networking><ClipboardRedirection>Disable</ClipboardRedirection><MemoryInMB>$MemoryInMB</MemoryInMB>
 <MappedFolders>
  <MappedFolder><HostFolder>$(XmlText $appPath)</HostFolder><SandboxFolder>C:\App</SandboxFolder><ReadOnly>true</ReadOnly></MappedFolder>
  <MappedFolder><HostFolder>$(XmlText $modelPath)</HostFolder><SandboxFolder>C:\Models</SandboxFolder><ReadOnly>true</ReadOnly></MappedFolder>
  <MappedFolder><HostFolder>$(XmlText $harnessPath)</HostFolder><SandboxFolder>C:\Harness</SandboxFolder><ReadOnly>true</ReadOnly></MappedFolder>
  <MappedFolder><HostFolder>$(XmlText $sandboxOutput)</HostFolder><SandboxFolder>C:\Evidence</SandboxFolder><ReadOnly>false</ReadOnly></MappedFolder>
 </MappedFolders>
 <LogonCommand><Command>powershell.exe -NoProfile -ExecutionPolicy Bypass -File C:\Harness\sandbox-worker.ps1</Command></LogonCommand>
</Configuration>
"@
$configPath = Join-Path $repoRoot 'test-output\clean-machine.wsb'
[IO.File]::WriteAllText($configPath, $config, [Text.UTF8Encoding]::new($false))
Write-Output "Generated $configPath. Open it on a host with Windows Sandbox installed; host files are read-only except the dedicated evidence folder."
