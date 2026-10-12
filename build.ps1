param([switch]$SkipRuntime, [switch]$NoZip, [string]$OutputName = 'LocalTranscriber')
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
if ($OutputName -notmatch '^[A-Za-z0-9-]+$') { throw 'OutputName must be a folder name containing letters, digits or hyphens' }
$releaseRoot = Join-Path $PSScriptRoot ('dist\' + $OutputName)
if (-not $SkipRuntime) {
    python scripts\prepare_runtime.py
    if ($LASTEXITCODE -ne 0) { throw 'Runtime preparation failed' }
}
dotnet publish src\LocalTranscriber\LocalTranscriber.csproj -c Release -r win-x64 --self-contained true -p:RestoreLockedMode=true -o $releaseRoot
if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
$nugetCache = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget\packages' }
Copy-Item -LiteralPath (Join-Path $nugetCache 'sqlite\3.53.4\LICENSE.txt') -Destination (Join-Path $PSScriptRoot 'assets\licences\SQLite-public-domain.txt') -Force
if (-not (Test-Path -LiteralPath '.tools\runtime')) { throw 'Prepare bundled runtimes before packaging' }
Copy-Item -LiteralPath '.tools\runtime' -Destination $releaseRoot -Recurse -Force
# Keep the workers this application uses, along with their DLLs and licence files.
Get-ChildItem -LiteralPath (Join-Path $releaseRoot 'runtime') -Recurse -File -Filter '*.exe' | Where-Object {
    ($_.FullName.Contains('\runtime\llama\') -and $_.Name -ne 'llama-server.exe') -or
    ($_.FullName.Contains('\runtime\whisper\') -and $_.Name -ne 'whisper-cli.exe') -or
    ($_.FullName.Contains('\runtime\ffmpeg\') -and $_.FullName -notin @((Join-Path $releaseRoot 'runtime\ffmpeg\ffmpeg.exe'),(Join-Path $releaseRoot 'runtime\ffmpeg\ffprobe.exe')))
} | ForEach-Object { if (-not $_.FullName.StartsWith($releaseRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected package path' }; Remove-Item -LiteralPath $_.FullName }
foreach ($path in @('README.md','THIRD_PARTY_NOTICES.md','LICENSE','assets\licences','assets\runtime-lock.json','workers\requirements.lock.txt')) {
    if (Test-Path -LiteralPath $path) { Copy-Item -LiteralPath $path -Destination $releaseRoot -Recurse -Force }
}
# Raw development reports and screenshots can contain private paths or recordings.
$packageDocs = Join-Path $releaseRoot 'docs'
if (Test-Path -LiteralPath $packageDocs) {
    if (-not ([IO.Path]::GetFullPath($packageDocs)).StartsWith($releaseRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected documentation path' }
    Remove-Item -LiteralPath $packageDocs -Recurse -Force
}
New-Item -ItemType Directory -Path $packageDocs -Force | Out-Null
Get-ChildItem -LiteralPath 'docs' -File -Filter '*.md' | Copy-Item -Destination $packageDocs -Force
# Bundle the MSVC CRT that native ONNX/ggml workers need; no end-user installer.
$crtFiles = @('vcruntime140.dll','vcruntime140_1.dll','msvcp140.dll','concrt140.dll')
foreach ($crtFile in $crtFiles) {
    $crtSource = Join-Path $env:WINDIR "System32\$crtFile"
    if (-not (Test-Path -LiteralPath $crtSource)) { throw "Native redistributable missing: $crtFile" }
    Get-ChildItem -LiteralPath (Join-Path $releaseRoot 'runtime') -Recurse -Filter '*.exe' | ForEach-Object { Copy-Item -LiteralPath $crtSource -Destination $_.DirectoryName -Force }
}
$inventory = Get-ChildItem -LiteralPath $releaseRoot -Recurse -File | Where-Object Name -ne 'bundle-manifest.json' | ForEach-Object { [pscustomobject]@{Path=[IO.Path]::GetRelativePath($releaseRoot,$_.FullName);Size=$_.Length;Sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()} }
$inventory | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $releaseRoot 'bundle-manifest.json')
if (-not $NoZip) {
    $zipPath = Join-Path $PSScriptRoot ('dist\' + $OutputName + '-portable.zip')
    if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath }
    [IO.Compression.ZipFile]::CreateFromDirectory($releaseRoot, $zipPath, [IO.Compression.CompressionLevel]::Fastest, $true)
    ((Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($zipPath)) | Set-Content -LiteralPath ($zipPath + '.sha256')
}
Write-Output "Portable app: $releaseRoot\LocalTranscriber.exe"
