#Requires -Version 5
# Generates 16 kHz mono WAV fixtures from manifest.json using Windows SAPI,
# appending trailing silence so the Silero VAD flushes each speech window.
[CmdletBinding()]
param(
    [string]$ManifestPath = "$PSScriptRoot/../tests/AIHelperNET.Integration.Tests/Fixtures/audio/manifest.json"
)

Add-Type -AssemblyName System.Speech
$ErrorActionPreference = 'Stop'

$manifest = Get-Content $ManifestPath -Raw | ConvertFrom-Json
$outDir   = Split-Path -Parent $ManifestPath
$silenceMs = [int]$manifest.trailingSilenceMs

# 16 kHz, 16-bit, mono — matches the pipeline's expected input rate (no resample needed).
$fmt = New-Object System.Speech.AudioFormat.SpeechAudioFormatInfo(16000, `
       [System.Speech.AudioFormat.AudioBitsPerSample]::Sixteen, `
       [System.Speech.AudioFormat.AudioChannel]::Mono)

function Find-DataChunkOffset([byte[]]$header) {
    # Locate the 'data' sub-chunk by scanning the RIFF chunk list.
    # Returns the offset of the 4-byte data-chunk-size field (i.e. the byte AFTER 'data').
    # SAPI writes an 18-byte extended fmt chunk, so the data chunk starts at 46, not 44.
    $i = 12  # skip RIFF header (12 bytes: 'RIFF' + size + 'WAVE')
    while ($i + 8 -le $header.Length) {
        $tag = [System.Text.Encoding]::ASCII.GetString($header[$i..($i+3)])
        $chunkSize = [System.BitConverter]::ToUInt32($header, $i + 4)
        if ($tag -eq 'data') {
            return $i + 4  # offset of the 32-bit data-chunk-size field
        }
        $i += 8 + $chunkSize
        if ($chunkSize -band 1) { $i++ }  # RIFF chunks are word-aligned
    }
    throw "No 'data' sub-chunk found in $wavPath"
}

function Append-Silence([string]$wavPath, [int]$ms) {
    # 16000 samples/s * 2 bytes/sample
    $silenceBytes = [int](16000 * 2 * $ms / 1000)
    $silence = New-Object byte[] $silenceBytes

    # Read just the header to locate the 'data' chunk offset dynamically.
    $header = New-Object byte[] 200
    $fsRead = [System.IO.File]::OpenRead($wavPath)
    try { [void]$fsRead.Read($header, 0, $header.Length) } finally { $fsRead.Close() }
    $dataSizeOffset = Find-DataChunkOffset $header

    $fs = [System.IO.File]::Open($wavPath, 'Open', 'ReadWrite')
    try {
        $fs.Seek(0, 'End') | Out-Null
        $fs.Write($silence, 0, $silence.Length)
        $newFileLen = [int]$fs.Length
        # Update data chunk size
        $newDataSize = $newFileLen - ($dataSizeOffset + 4)
        $fs.Seek($dataSizeOffset, 'Begin') | Out-Null
        $w = New-Object System.IO.BinaryWriter($fs)
        $w.Write([int]$newDataSize)            # data chunk size
        # Update RIFF chunk size
        $fs.Seek(4, 'Begin') | Out-Null
        $w.Write([int]($newFileLen - 8))       # RIFF chunk size
        $w.Flush()
    } finally { $fs.Close() }
}

foreach ($line in $manifest.lines) {
    $path = Join-Path $outDir $line.file
    $synth = New-Object System.Speech.Synthesis.SpeechSynthesizer
    try {
        $synth.SetOutputToWaveFile($path, $fmt)
        $synth.Speak($line.text)
    } finally { $synth.Dispose() }
    Append-Silence $path $silenceMs
    Write-Host "Generated $($line.file): '$($line.text)' (+${silenceMs}ms silence)"
}
Write-Host "Done. $($manifest.lines.Count) fixtures written to $outDir"
