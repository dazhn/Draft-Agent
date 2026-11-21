param(
    [string]$ComfyRoot = (Join-Path $env:USERPROFILE "Pictures\ComfyUI"),
    [string]$MirrorBaseUrl = "https://hf-mirror.com",
    [switch]$PatchFiles,
    [switch]$SetUserEnvironment,
    [string]$ReportPath = (Join-Path $PSScriptRoot "hf-mirror-report.json")
)

$ErrorActionPreference = "Stop"

function Get-RelativePathSafe([string]$BasePath, [string]$FullPath) {
    $baseUri = [Uri]((Resolve-Path -LiteralPath $BasePath).Path.TrimEnd('\') + '\')
    $fileUri = [Uri](Resolve-Path -LiteralPath $FullPath).Path
    return [Uri]::UnescapeDataString($baseUri.MakeRelativeUri($fileUri).ToString()).Replace('/', '\')
}

function Read-TextFile([string]$Path) {
    try {
        return [System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8)
    }
    catch {
        return $null
    }
}

function Write-Utf8NoBom([string]$Path, [string]$Text) {
    $encoding = [System.Text.UTF8Encoding]::new($false)
    [System.IO.File]::WriteAllText($Path, $Text, $encoding)
}

function Backup-File([string]$SourcePath, [string]$RootPath, [string]$BackupRoot) {
    $relative = Get-RelativePathSafe $RootPath $SourcePath
    $target = Join-Path $BackupRoot $relative
    $targetDirectory = Split-Path -Parent $target
    if (-not [string]::IsNullOrWhiteSpace($targetDirectory)) {
        New-Item -ItemType Directory -Force -Path $targetDirectory | Out-Null
    }

    Copy-Item -LiteralPath $SourcePath -Destination $target -Force
    return $target
}

$mirror = $MirrorBaseUrl.TrimEnd('/')
$mirrorHost = ([Uri]$mirror).Host
$pluginRoot = Join-Path $ComfyRoot "custom_nodes\tts_audio_suite"
if (-not (Test-Path -LiteralPath $pluginRoot)) {
    throw "Cannot find tts_audio_suite under: $pluginRoot"
}

$env:HF_ENDPOINT = $mirror
$env:HUGGINGFACE_HUB_BASE_URL = $mirror
$env:HF_HUB_ENDPOINT = $mirror

if ($SetUserEnvironment) {
    [Environment]::SetEnvironmentVariable("HF_ENDPOINT", $mirror, "User")
    [Environment]::SetEnvironmentVariable("HUGGINGFACE_HUB_BASE_URL", $mirror, "User")
    [Environment]::SetEnvironmentVariable("HF_HUB_ENDPOINT", $mirror, "User")
}

$extensions = @(".py", ".json", ".yaml", ".yml", ".toml", ".md", ".txt")
$files = Get-ChildItem -LiteralPath $pluginRoot -Recurse -File |
    Where-Object { $extensions -contains $_.Extension.ToLowerInvariant() }

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$backupRoot = Join-Path $PSScriptRoot ("hf-mirror-backup\" + $timestamp)
$changes = [System.Collections.Generic.List[object]]::new()
$hardcodedPatterns = @(
    "https://huggingface.co",
    "http://huggingface.co",
    "huggingface.co"
)

foreach ($file in $files) {
    $text = Read-TextFile $file.FullName
    if ($null -eq $text) {
        continue
    }

    $hitCount = 0
    foreach ($pattern in $hardcodedPatterns) {
        $hitCount += ([regex]::Matches($text, [regex]::Escape($pattern))).Count
    }

    if ($hitCount -eq 0) {
        continue
    }

    $relative = Get-RelativePathSafe $pluginRoot $file.FullName
    $backupPath = $null
    if ($PatchFiles) {
        $backupPath = Backup-File $file.FullName $pluginRoot $backupRoot
        $patched = $text.
            Replace("https://huggingface.co", $mirror).
            Replace("http://huggingface.co", $mirror).
            Replace("huggingface.co", $mirrorHost)
        Write-Utf8NoBom $file.FullName $patched
    }

    $changes.Add([ordered]@{
        path = $relative
        occurrences = $hitCount
        patched = [bool]$PatchFiles
        backup = $backupPath
    })
}

$envFile = Join-Path $PSScriptRoot "comfy-hf-mirror.env.ps1"
$envContent = @"
`$env:HF_ENDPOINT = "$mirror"
`$env:HUGGINGFACE_HUB_BASE_URL = "$mirror"
`$env:HF_HUB_ENDPOINT = "$mirror"
Write-Host "HF mirror enabled: $mirror"
"@
Write-Utf8NoBom $envFile $envContent

$report = [ordered]@{
    mirror = $mirror
    comfyRoot = (Resolve-Path -LiteralPath $ComfyRoot).Path
    pluginRoot = (Resolve-Path -LiteralPath $pluginRoot).Path
    setUserEnvironment = [bool]$SetUserEnvironment
    patchedFiles = [bool]$PatchFiles
    envFile = $envFile
    backupRoot = if ($PatchFiles) { $backupRoot } else { $null }
    filesWithHuggingFaceLinks = $changes.Count
    changes = $changes
}

Write-Utf8NoBom $ReportPath ($report | ConvertTo-Json -Depth 10)
Write-Host "HF_ENDPOINT=$mirror"
Write-Host "Env file: $envFile"
Write-Host "Report: $ReportPath"
Write-Host "Files with hardcoded huggingface.co links: $($changes.Count)"
if ($PatchFiles) {
    Write-Host "Patched files and backups are under: $backupRoot"
}
else {
    Write-Host "Dry run only. Re-run with -PatchFiles to replace hardcoded URLs after review."
}
Write-Host "Restart ComfyUI after applying environment variables or patching files."
