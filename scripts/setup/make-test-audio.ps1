# SPDX-License-Identifier: AGPL-3.0-or-later
# Copyright (c) 2026 Robert Binsack
<#
.SYNOPSIS
  Speaks a text with a speech synthesis voice of Windows into a WAV file (16 kHz, 16 bit, mono),
  the format the speech recognition expects. Used to make test audio on the spot, so that no
  recorded audio has to be shipped.
.PARAMETER Text
  The text to speak. Alternatively -TextFile (UTF-8).
.PARAMETER OutFile
  The WAV file to write.
.PARAMETER Language
  en or de. A voice of that language must be installed in Windows.
.PARAMETER Rate
  Speaking rate from -10 (slow) to 10 (fast), default 0.
.PARAMETER ListVoices
  Only lists the installed voices.
.OUTPUTS
  Exit code 0: file written. 2: wrong call. 3: no voice for the language installed. 1: other error.
.EXAMPLE
  .\make-test-audio.ps1 -Text 'The invoice was saved.' -Language en -OutFile test-en.wav
#>
param(
    [string]$Text,
    [string]$TextFile,
    [string]$OutFile,
    [ValidateSet('en', 'de')][string]$Language = 'en',
    [ValidateRange(-10, 10)][int]$Rate = 0,
    [switch]$ListVoices
)
$ErrorActionPreference = 'Stop'
try {
    Add-Type -AssemblyName System.Speech
    $synth = New-Object System.Speech.Synthesis.SpeechSynthesizer
} catch {
    Write-Host "speech synthesis of Windows is not available: $($_.Exception.Message)"
    exit 1
}
try {
    $voices = @($synth.GetInstalledVoices() | Where-Object { $_.Enabled } | ForEach-Object { $_.VoiceInfo })
    if ($ListVoices) {
        foreach ($v in $voices) { Write-Host ("{0}  {1}" -f $v.Culture.Name, $v.Name) }
        if (-not $voices.Count) { Write-Host 'no speech synthesis voice is installed' }
        exit 0
    }
    if ($TextFile) { $Text = [System.IO.File]::ReadAllText($TextFile, [System.Text.Encoding]::UTF8) }
    if (-not $Text -or -not $OutFile) {
        Write-Host 'usage: make-test-audio.ps1 -Text <text> | -TextFile <file> -OutFile <file.wav> [-Language en|de] [-Rate n]'
        exit 2
    }
    $voice = $voices | Where-Object { $_.Culture.TwoLetterISOLanguageName -eq $Language } | Select-Object -First 1
    if (-not $voice) {
        $have = ($voices | ForEach-Object { $_.Culture.Name }) -join ', '
        if (-not $have) { $have = 'none' }
        Write-Host "no speech synthesis voice for language '$Language' is installed (installed: $have)"
        exit 3
    }
    $full = [System.IO.Path]::GetFullPath($OutFile)
    $dir = [System.IO.Path]::GetDirectoryName($full)
    if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Force $dir | Out-Null }
    $format = New-Object System.Speech.AudioFormat.SpeechAudioFormatInfo(16000, [System.Speech.AudioFormat.AudioBitsPerSample]::Sixteen, [System.Speech.AudioFormat.AudioChannel]::Mono)
    $synth.SelectVoice($voice.Name)
    $synth.Rate = $Rate
    $synth.SetOutputToWaveFile($full, $format)
    $synth.Speak($Text)
    $synth.SetOutputToNull()
    Write-Host "written: $full (voice: $($voice.Name))"
    exit 0
} catch {
    Write-Host "speech synthesis failed: $($_.Exception.Message)"
    exit 1
} finally {
    $synth.Dispose()
}
