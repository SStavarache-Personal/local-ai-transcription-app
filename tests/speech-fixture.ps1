param([string]$OutputFolder)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Speech
$synth = New-Object System.Speech.Synthesis.SpeechSynthesizer
$voices = $synth.GetInstalledVoices() | ForEach-Object { $_.VoiceInfo }
$english = 'Welcome to the Local Transcriber project meeting. We agreed to test the Windows application on Friday. Marie will review the French transcript. The budget is two thousand dollars. Please keep the original recording unchanged.'
$french = 'Bonjour et bienvenue à la réunion du projet Local Transcriber. Nous avons décidé de tester cette application vendredi. Marie va vérifier la transcription française. Le budget est de deux mille dollars. Il faut conserver le fichier original.'
foreach ($fixture in @(@{Name='english';Culture='en';Text=$english},@{Name='french';Culture='fr';Text=$french})) {
    $voice = $voices | Where-Object { $_.Culture.Name.StartsWith($fixture.Culture) } | Select-Object -First 1
    if (-not $voice) { Write-Warning "No installed $($fixture.Culture) speech voice; fixture unavailable"; continue }
    $synth.SelectVoice($voice.Name)
    $synth.SetOutputToWaveFile((Join-Path $OutputFolder ($fixture.Name + '.wav')))
    $synth.Speak($fixture.Text)
    $synth.SetOutputToNull()
    [IO.File]::WriteAllText((Join-Path $OutputFolder ($fixture.Name + '.txt')), $fixture.Text, (New-Object Text.UTF8Encoding($false)))
    Write-Output "Generated $($fixture.Name) with $($voice.Name)"
}
$synth.Dispose()
