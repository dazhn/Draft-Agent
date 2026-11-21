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
    [string]$PromptIdPath = (Join-Path $PSScriptRoot "last-index-tts2-prompt.json")
)

$ErrorActionPreference = "Stop"

function Join-Endpoint([string]$Base, [string]$Path) {
    return $Base.TrimEnd("/") + "/" + $Path.TrimStart("/")
}

$endpoint = $Endpoint.TrimEnd("/")
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

$clientId = "draft-agent-script-{0}" -f [guid]::NewGuid().ToString("N")
$body = @{
    client_id = $clientId
    prompt = $prompt
} | ConvertTo-Json -Depth 30

$submitted = Invoke-RestMethod -Method Post -Uri (Join-Endpoint $endpoint "prompt") -Body $body -ContentType "application/json; charset=utf-8" -TimeoutSec 30
$promptId = [string]$submitted.prompt_id
if ([string]::IsNullOrWhiteSpace($promptId)) {
    throw "ComfyUI did not return prompt_id: $($submitted | ConvertTo-Json -Depth 20)"
}

$record = [ordered]@{
    endpoint = $endpoint
    promptId = $promptId
    clientId = $clientId
    text = $Text
    voiceName = $VoiceName
    modelPath = $ModelPath
    createdAt = (Get-Date).ToString("o")
    response = $submitted
}

$encoding = [System.Text.UTF8Encoding]::new($false)
[System.IO.File]::WriteAllText($PromptIdPath, ($record | ConvertTo-Json -Depth 20), $encoding)
Write-Host "prompt_id=$promptId"
Write-Host "client_id=$clientId"
Write-Host "promptRecord=$PromptIdPath"
