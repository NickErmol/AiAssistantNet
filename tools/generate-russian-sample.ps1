#Requires -Version 5
# One-off: generate a Russian-language 16 kHz mono WAV containing embedded English
# technical terms, to verify Whisper language auto-detection. Mirrors the format and
# trailing-silence logic of generate-audio-fixtures.ps1.
[CmdletBinding()]
param(
    [string]$OutDir = "$PSScriptRoot/../tests/AIHelperNET.Integration.Tests/Fixtures/audio",
    [string]$File   = "ru_mixed.wav",
    [int]$SilenceMs = 800,
    [string]$Text   = "Объясни, пожалуйста, что такое dependency injection и зачем нужен паттерн singleton в системном дизайне."
)

Add-Type -AssemblyName System.Speech
$ErrorActionPreference = 'Stop'

$fmt = New-Object System.Speech.AudioFormat.SpeechAudioFormatInfo(16000, `
       [System.Speech.AudioFormat.AudioBitsPerSample]::Sixteen, `
       [System.Speech.AudioFormat.AudioChannel]::Mono)

function Find-DataChunkOffset([byte[]]$header) {
    $i = 12
    while ($i + 8 -le $header.Length) {
        $tag = [System.Text.Encoding]::ASCII.GetString($header[$i..($i+3)])
        $chunkSize = [System.BitConverter]::ToUInt32($header, $i + 4)
        if ($tag -eq 'data') { return $i + 4 }
        $i += 8 + $chunkSize
        if ($chunkSize -band 1) { $i++ }
    }
    throw "No 'data' sub-chunk found"
}

function Append-Silence([string]$wavPath, [int]$ms) {
    $silence = New-Object byte[] ([int](16000 * 2 * $ms / 1000))
    $header = New-Object byte[] 200
    $fsRead = [System.IO.File]::OpenRead($wavPath)
    try { [void]$fsRead.Read($header, 0, $header.Length) } finally { $fsRead.Close() }
    $dataSizeOffset = Find-DataChunkOffset $header
    $fs = [System.IO.File]::Open($wavPath, 'Open', 'ReadWrite')
    try {
        $fs.Seek(0, 'End') | Out-Null
        $fs.Write($silence, 0, $silence.Length)
        $newFileLen = [int]$fs.Length
        $w = New-Object System.IO.BinaryWriter($fs)
        $fs.Seek($dataSizeOffset, 'Begin') | Out-Null
        $w.Write([int]($newFileLen - ($dataSizeOffset + 4)))
        $fs.Seek(4, 'Begin') | Out-Null
        $w.Write([int]($newFileLen - 8))
        $w.Flush()
    } finally { $fs.Close() }
}

$path = Join-Path $OutDir $File
$synth = New-Object System.Speech.Synthesis.SpeechSynthesizer
try {
    $ru = $synth.GetInstalledVoices() | Where-Object { $_.VoiceInfo.Culture.Name -like 'ru*' } | Select-Object -First 1
    if (-not $ru) { throw "No Russian (ru-*) SAPI voice installed." }
    $synth.SelectVoice($ru.VoiceInfo.Name)
    $synth.SetOutputToWaveFile($path, $fmt)
    $synth.Speak($Text)
} finally { $synth.Dispose() }
Append-Silence $path $SilenceMs
Write-Host "Generated $File using voice '$($ru.VoiceInfo.Name)': '$Text' (+${SilenceMs}ms silence)"
