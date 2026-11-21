param(
    [string]$Endpoint = "http://127.0.0.1:8000",
    [string]$PromptId = "",
    [string]$PromptIdPath = (Join-Path $PSScriptRoot "last-index-tts2-prompt.json"),
    [double]$TimeoutSeconds = 0
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

function Find-TempDirectory($SystemStats) {
    $argv = @($SystemStats.system.argv)
    for ($i = 0; $i -lt $argv.Count - 1; $i++) {
        if ($argv[$i] -eq "--base-directory") {
            return (Join-Path $argv[$i + 1] "temp")
        }
    }

    return $null
}

function Collect-Assets($Value, [System.Collections.Generic.List[object]]$Results) {
    if ($null -eq $Value) {
        return
    }

    if ($Value -is [System.Array]) {
        foreach ($item in $Value) {
            Collect-Assets $item $Results
        }
        return
    }

    if ($Value -is [pscustomobject]) {
        $names = $Value.PSObject.Properties.Name
        if ($names -contains "filename") {
            $filename = [string]$Value.filename
            if (-not [string]::IsNullOrWhiteSpace($filename)) {
                $assetType = if ($names -contains "type" -and -not [string]::IsNullOrWhiteSpace([string]$Value.type)) { [string]$Value.type } else { "output" }
                $subfolder = if ($names -contains "subfolder") { [string]$Value.subfolder } else { "" }
                $Results.Add([ordered]@{
                    filename = $filename
                    type = $assetType
                    subfolder = $subfolder
                })
            }
        }

        foreach ($property in $Value.PSObject.Properties) {
            Collect-Assets $property.Value $Results
        }
    }
}

function Test-PromptInQueue($Queue, [string]$PromptId) {
    $json = $Queue | ConvertTo-Json -Depth 20 -Compress
    return $json.Contains($PromptId)
}

if ([string]::IsNullOrWhiteSpace($PromptId) -and (Test-Path -LiteralPath $PromptIdPath)) {
    $record = Get-Content -LiteralPath $PromptIdPath -Raw | ConvertFrom-Json
    $PromptId = [string]$record.promptId
    if ([string]::IsNullOrWhiteSpace($Endpoint) -and -not [string]::IsNullOrWhiteSpace([string]$record.endpoint)) {
        $Endpoint = [string]$record.endpoint
    }
}

if ([string]::IsNullOrWhiteSpace($PromptId)) {
    throw "PromptId is required or $PromptIdPath must exist."
}

$endpoint = $Endpoint.TrimEnd("/")
$stats = Invoke-RestMethod -Method Get -Uri (Join-Endpoint $endpoint "system_stats") -TimeoutSec 10
$outputDirectory = Find-OutputDirectory $stats
$tempDirectory = Find-TempDirectory $stats
$deadline = if ($TimeoutSeconds -gt 0) { (Get-Date).AddSeconds($TimeoutSeconds) } else { Get-Date }

do {
    $history = Invoke-RestMethod -Method Get -Uri (Join-Endpoint $endpoint "history/$PromptId") -TimeoutSec 20
    if ($history.PSObject.Properties.Name -contains $PromptId) {
        $record = $history.$PromptId
        $statusJson = $record.status | ConvertTo-Json -Depth 20 -Compress
        $assets = [System.Collections.Generic.List[object]]::new()
        Collect-Assets $record.outputs $assets
        Write-Host "state=history"
        Write-Host "status=$statusJson"
        $seenAssets = @{}
        foreach ($asset in $assets) {
            $type = [string]$asset["type"]
            $subfolder = [string]$asset["subfolder"]
            $filename = [string]$asset["filename"]
            $assetKey = "$type|$subfolder|$filename"
            if ($seenAssets.ContainsKey($assetKey)) {
                continue
            }

            $seenAssets[$assetKey] = $true
            $query = "filename={0}&type={1}" -f [uri]::EscapeDataString($filename), [uri]::EscapeDataString($type)
            if (-not [string]::IsNullOrWhiteSpace($subfolder)) {
                $query += "&subfolder={0}" -f [uri]::EscapeDataString($subfolder)
            }

            $basePath = if ($type -eq "temp") { $tempDirectory } else { $outputDirectory }
            $relativePath = $filename
            if (-not [string]::IsNullOrWhiteSpace($subfolder)) {
                $relativePath = Join-Path $subfolder $filename
            }

            $localPath = if ([string]::IsNullOrWhiteSpace($basePath)) { "" } else { Join-Path $basePath $relativePath }
            Write-Host "asset=$type|$subfolder|$filename"
            Write-Host "view=$(Join-Endpoint $endpoint "view?$query")"
            if (-not [string]::IsNullOrWhiteSpace($localPath)) {
                Write-Host "path=$localPath"
            }
        }

        if ($statusJson -match "error") {
            exit 2
        }

        exit 0
    }

    $queue = Invoke-RestMethod -Method Get -Uri (Join-Endpoint $endpoint "queue") -TimeoutSec 20
    if (Test-PromptInQueue $queue $PromptId) {
        Write-Host "state=queue"
        if ($TimeoutSeconds -gt 0 -and (Get-Date) -lt $deadline) {
            Start-Sleep -Seconds 2
            continue
        }

        exit 1
    }

    Write-Host "state=not_found"
    if ($TimeoutSeconds -gt 0 -and (Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 2
    }
    else {
        exit 1
    }
} while ((Get-Date) -lt $deadline)

throw "Timed out waiting for prompt_id=$PromptId"
