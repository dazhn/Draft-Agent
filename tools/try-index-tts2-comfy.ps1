param(
    [string]$Endpoint = "http://127.0.0.1:8000",
    [string]$Text = "Dui bu qi, wo lai wan le.",
    [string]$VoiceName = "ComfyUI_00038_.flac",
    [double]$Happy = 0,
    [double]$Angry = 0,
    [double]$Sad = 0,
    [double]$Surprised = 0,
    [double]$Afraid = 0.8,
    [double]$Disgusted = 0,
    [double]$Calm = 0,
    [double]$Melancholic = 0,
    [string]$ModelPath = "local:IndexTTS-2",
    [double]$TimeoutSeconds = 240
)

$ErrorActionPreference = "Stop"

function Join-Endpoint([string]$Base, [string]$Path) {
    return $Base.TrimEnd("/") + "/" + $Path.TrimStart("/")
}

function Find-OutputDirectory($SystemStats) {
    $argv = @($SystemStats.system.argv)
    for ($i = 0; $i -lt $argv.Count - 1; $i++) {
        if ($argv[$i] -eq "--output-directory") {
            return $argv[$i + 1]
        }
    }

    return $null
}

function Collect-Filenames($Value, [System.Collections.Generic.List[string]]$Results) {
    if ($null -eq $Value) {
        return
    }

    if ($Value -is [System.Array]) {
        foreach ($item in $Value) {
            Collect-Filenames $item $Results
        }
        return
    }

    if ($Value -is [pscustomobject]) {
        foreach ($property in $Value.PSObject.Properties) {
            if ($property.Name -eq "filename" -and -not [string]::IsNullOrWhiteSpace([string]$property.Value)) {
                $Results.Add([string]$property.Value)
            }

            Collect-Filenames $property.Value $Results
        }
    }
}

function Test-PromptInQueue($Queue, [string]$PromptId) {
    $json = $Queue | ConvertTo-Json -Depth 20 -Compress
    return $json.Contains($PromptId)
}

$endpoint = $Endpoint.TrimEnd("/")
$stats = Invoke-RestMethod -Method Get -Uri (Join-Endpoint $endpoint "system_stats") -TimeoutSec 10
$outputDirectory = Find-OutputDirectory $stats

$prompt = [ordered]@{
    "125" = @{
        class_type = "IndexTTSEmotionOptionsNode"
        inputs = @{
            Happy = $Happy
            Angry = $Angry
            Sad = $Sad
            Surprised = $Surprised
            Afraid = $Afraid
            Disgusted = $Disgusted
            Calm = $Calm
            Melancholic = $Melancholic
        }
    }
    "123" = @{
        class_type = "IndexTTSEngineNode"
        inputs = @{
            model_path = $ModelPath
            device = "auto"
            emotion_alpha = 0.9
            use_random = $false
            max_text_tokens_per_segment = 120
            interval_silence = 200
            temperature = 0.8
            top_p = 0.8
            top_k = 30
            do_sample = $true
            length_penalty = 0
            num_beams = 3
            repetition_penalty = 9.5
            max_mel_tokens = 1500
            use_fp16 = $true
            use_deepspeed = $false
            emotion_control = @("125", 0)
            use_cuda_kernel = "auto"
            use_torch_compile = $false
            use_accel = $false
            stream_return = $false
            more_segment_before = 0
            low_vram = $false
        }
    }
    "51" = @{
        class_type = "CharacterVoicesNode"
        inputs = @{
            voice_name = $VoiceName
            reference_text = ""
        }
    }
    "47" = @{
        class_type = "UnifiedTTSTextNode"
        inputs = @{
            TTS_engine = @("123", 0)
            opt_narrator = @("51", 0)
            text = $Text
            narrator_voice = "none"
            seed = [int](Get-Random -Minimum 1 -Maximum 2147483647)
            enable_chunking = $true
            max_chars_per_chunk = 400
            chunk_combination_method = "auto"
            silence_between_chunks_ms = 100
            enable_audio_cache = $false
            batch_size = 0
        }
    }
    "15" = @{
        class_type = "PreviewAudio"
        inputs = @{
            audio = @("47", 0)
        }
    }
}

$body = @{
    client_id = ("draft-agent-script-{0}" -f [guid]::NewGuid().ToString("N"))
    prompt = $prompt
} | ConvertTo-Json -Depth 30

Write-Host "Submitting IndexTTS-2 prompt to $endpoint ..."
$submitted = Invoke-RestMethod -Method Post -Uri (Join-Endpoint $endpoint "prompt") -Body $body -ContentType "application/json; charset=utf-8" -TimeoutSec 30
$promptId = [string]$submitted.prompt_id
if ([string]::IsNullOrWhiteSpace($promptId)) {
    throw "ComfyUI did not return prompt_id: $($submitted | ConvertTo-Json -Depth 20)"
}

Write-Host "prompt_id=$promptId"
$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 2
    $history = Invoke-RestMethod -Method Get -Uri (Join-Endpoint $endpoint "history/$promptId") -TimeoutSec 20
    if ($history.PSObject.Properties.Name -contains $promptId) {
        $record = $history.$promptId
        $statusJson = $record.status | ConvertTo-Json -Depth 20 -Compress
        if ($statusJson -match "error") {
            throw "ComfyUI history reports error: $statusJson"
        }

        $filenames = [System.Collections.Generic.List[string]]::new()
        Collect-Filenames $record.outputs $filenames
        $unique = $filenames | Select-Object -Unique
        Write-Host "completed=true"
        Write-Host "outputDirectory=$outputDirectory"
        foreach ($filename in $unique) {
            $viewUrl = (Join-Endpoint $endpoint ("view?filename={0}" -f [uri]::EscapeDataString($filename))) + "&type=output"
            $localPath = if ([string]::IsNullOrWhiteSpace($outputDirectory)) { "" } else { Join-Path $outputDirectory $filename }
            Write-Host "filename=$filename"
            Write-Host "view=$viewUrl"
            if (-not [string]::IsNullOrWhiteSpace($localPath)) {
                Write-Host "path=$localPath"
            }
        }
        exit 0
    }

    $queue = Invoke-RestMethod -Method Get -Uri (Join-Endpoint $endpoint "queue") -TimeoutSec 20
    if (Test-PromptInQueue $queue $promptId) {
        Write-Host "waiting=$promptId"
        continue
    }

    Write-Host "waiting_for_history=$promptId"
}

throw "Timed out waiting for prompt_id=$promptId"
