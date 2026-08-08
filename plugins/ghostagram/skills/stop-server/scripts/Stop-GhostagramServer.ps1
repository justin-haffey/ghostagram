[CmdletBinding()]
param(
    [string] $Url = "http://127.0.0.1:5256",
    [ValidateRange(1, 60)]
    [int] $ShutdownTimeoutSeconds = 10
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Resolve-LocalUrl {
    param([string] $Value)

    $uri = [Uri] $Value
    if ($uri.Scheme -ne "http" -or -not $uri.IsLoopback -or $uri.AbsolutePath -ne "/") {
        throw "Url must be an HTTP loopback base URL such as http://127.0.0.1:5256."
    }

    return $uri.GetLeftPart([UriPartial]::Authority).TrimEnd("/")
}

function Get-StatePath {
    param([string] $RepositoryRoot, [string] $BaseUrl)

    $identity = ($RepositoryRoot.TrimEnd("\") + "|" + $BaseUrl).ToLowerInvariant()
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [Text.Encoding]::UTF8.GetBytes($identity)
        $hash = ([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace("-", "").Substring(0, 16).ToLowerInvariant()
    }
    finally {
        $sha.Dispose()
    }

    return Join-Path (Join-Path (Join-Path ([IO.Path]::GetTempPath()) "ghostagram-codex") $hash) "server-state.json"
}

function Test-Health {
    param([string] $HealthUrl)

    try {
        $response = Invoke-RestMethod -Uri $HealthUrl -Method Get -TimeoutSec 2
        return $response.status -eq "ok"
    }
    catch {
        return $false
    }
}

$startedAt = [Diagnostics.Stopwatch]::StartNew()
$baseUrl = Resolve-LocalUrl $Url
$healthUrl = "$baseUrl/healthz"
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..\..\..")).Path.TrimEnd("\")
$expectedProjectPath = Join-Path $repositoryRoot "src\Ghostagram.Server\Ghostagram.Server.csproj"
$statePath = Get-StatePath $repositoryRoot $baseUrl

if (-not (Test-Path -LiteralPath $statePath -PathType Leaf)) {
    $startedAt.Stop()
    [pscustomobject]@{
        status = "not-managed"
        managed = $false
        healthy = Test-Health $healthUrl
        pid = $null
        url = $baseUrl
        elapsedMs = $startedAt.ElapsedMilliseconds
    } | ConvertTo-Json -Compress
    exit 0
}

try {
    $state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
}
catch {
    throw "The Ghostagram ownership state is unreadable. No process was stopped. State: $statePath"
}

if ($state.owner -ne "ghostagram-start-server" -or
    $state.repositoryRoot -ne $repositoryRoot -or
    $state.projectPath -ne $expectedProjectPath -or
    $state.url -ne $baseUrl -or
    [int] $state.pid -le 0) {
    throw "Ghostagram ownership-mismatch. No process was stopped. State: $statePath"
}

$processId = [int] $state.pid
$process = Get-Process -Id $processId -ErrorAction SilentlyContinue
if ($null -eq $process) {
    Remove-Item -LiteralPath $statePath
    $startedAt.Stop()
    [pscustomobject]@{
        status = "already-stopped"
        managed = $true
        healthy = Test-Health $healthUrl
        pid = $processId
        url = $baseUrl
        elapsedMs = $startedAt.ElapsedMilliseconds
    } | ConvertTo-Json -Compress
    exit 0
}

try {
    $sameStartTime = $process.StartTime.ToUniversalTime().Ticks -eq [long] $state.processStartTimeUtcTicks
    $sameExecutable = [string]::Equals($process.Path, [string] $state.executablePath, [StringComparison]::OrdinalIgnoreCase)
}
catch {
    throw "Ghostagram ownership could not be verified. No process was stopped. PID: $processId"
}

if (-not $sameStartTime -or -not $sameExecutable) {
    throw "Ghostagram ownership-mismatch. No process was stopped. PID: $processId"
}

Stop-Process -Id $processId
$deadline = [DateTime]::UtcNow.AddSeconds($ShutdownTimeoutSeconds)
while ([DateTime]::UtcNow -lt $deadline -and $null -ne (Get-Process -Id $processId -ErrorAction SilentlyContinue)) {
    Start-Sleep -Milliseconds 100
}

if ($null -ne (Get-Process -Id $processId -ErrorAction SilentlyContinue)) {
    throw "The owned Ghostagram process $processId did not stop within $ShutdownTimeoutSeconds seconds."
}

Remove-Item -LiteralPath $statePath
$startedAt.Stop()
[pscustomobject]@{
    status = "stopped"
    managed = $true
    healthy = Test-Health $healthUrl
    pid = $processId
    url = $baseUrl
    elapsedMs = $startedAt.ElapsedMilliseconds
} | ConvertTo-Json -Compress
