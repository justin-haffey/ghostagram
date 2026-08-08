[CmdletBinding()]
param(
    [string] $Url = "http://127.0.0.1:5256",
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Debug",
    [ValidateRange(1, 120)]
    [int] $ReadinessTimeoutSeconds = 20,
    [switch] $ForceBuild
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

function Get-RuntimePaths {
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

    $root = Join-Path (Join-Path ([IO.Path]::GetTempPath()) "ghostagram-codex") $hash
    return [pscustomobject]@{
        Root = $root
        State = Join-Path $root "server-state.json"
    }
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

function Get-OwnedProcess {
    param([string] $StatePath, [string] $RepositoryRoot, [string] $BaseUrl)

    if (-not (Test-Path -LiteralPath $StatePath -PathType Leaf)) {
        return $null
    }

    try {
        $state = Get-Content -LiteralPath $StatePath -Raw | ConvertFrom-Json
        if ($state.owner -ne "ghostagram-start-server" -or
            $state.repositoryRoot -ne $RepositoryRoot -or
            $state.url -ne $BaseUrl -or
            [int] $state.pid -le 0) {
            return $null
        }

        $process = Get-Process -Id ([int] $state.pid) -ErrorAction SilentlyContinue
        if ($null -eq $process) {
            return $null
        }

        if ($process.StartTime.ToUniversalTime().Ticks -ne [long] $state.processStartTimeUtcTicks) {
            return $null
        }

        if (-not [string]::Equals($process.Path, [string] $state.executablePath, [StringComparison]::OrdinalIgnoreCase)) {
            return $null
        }

        return [pscustomobject]@{ Process = $process; State = $state }
    }
    catch {
        return $null
    }
}

function Write-State {
    param([string] $Path, [object] $Value)

    $temporaryPath = "$Path.$PID.tmp"
    $Value | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $temporaryPath -Encoding UTF8
    Move-Item -LiteralPath $temporaryPath -Destination $Path -Force
}

function Test-BuildRequired {
    param([string] $ExecutablePath, [string[]] $SourceRoots)

    if (-not (Test-Path -LiteralPath $ExecutablePath -PathType Leaf)) {
        return $true
    }

    $builtAt = (Get-Item -LiteralPath $ExecutablePath).LastWriteTimeUtc
    $extensions = @(".cs", ".razor", ".csproj", ".props", ".targets", ".resx", ".json", ".js", ".css", ".html")
    foreach ($sourceRoot in $SourceRoots) {
        $newerInput = Get-ChildItem -LiteralPath $sourceRoot -Recurse -File | Where-Object {
            $_.FullName -notmatch "[\\/](bin|obj)[\\/]" -and
            $extensions -contains $_.Extension.ToLowerInvariant() -and
            $_.LastWriteTimeUtc -gt $builtAt
        } | Select-Object -First 1

        if ($null -ne $newerInput) {
            return $true
        }
    }

    return $false
}

$startedAt = [Diagnostics.Stopwatch]::StartNew()
$baseUrl = Resolve-LocalUrl $Url
$healthUrl = "$baseUrl/healthz"
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..\..\..")).Path.TrimEnd("\")
$projectDirectory = Join-Path $repositoryRoot "src\Ghostagram.Server"
$projectPath = Join-Path $projectDirectory "Ghostagram.Server.csproj"
$targetFramework = "net10.0"
$executablePath = Join-Path $projectDirectory "bin\$Configuration\$targetFramework\Ghostagram.Server.exe"
$runtime = Get-RuntimePaths $repositoryRoot $baseUrl
New-Item -ItemType Directory -Path $runtime.Root -Force | Out-Null

$owned = Get-OwnedProcess $runtime.State $repositoryRoot $baseUrl
if (Test-Health $healthUrl) {
    $startedAt.Stop()
    $status = if ($null -ne $owned) { "reused-owned" } else { "reused-unmanaged" }
    [pscustomobject]@{
        status = $status
        healthy = $true
        managed = $null -ne $owned
        pid = if ($null -ne $owned) { $owned.Process.Id } else { $null }
        url = $baseUrl
        healthUri = $healthUrl
        build = "skipped"
        elapsedMs = $startedAt.ElapsedMilliseconds
    } | ConvertTo-Json -Compress
    exit 0
}

if ($null -ne $owned) {
    throw "The skill-owned Ghostagram process $($owned.Process.Id) is running but unhealthy. Use `$stop-server for $baseUrl, then retry."
}

if (Test-Path -LiteralPath $runtime.State) {
    Remove-Item -LiteralPath $runtime.State
}

$sourceRoots = @(
    (Join-Path $repositoryRoot "src\Ghostagram.Server"),
    (Join-Path $repositoryRoot "src\Ghostagram.Blazor"),
    (Join-Path $repositoryRoot "src\Ghostagram.Contracts"),
    (Join-Path $repositoryRoot "src\Ghostagram.Core")
)
$buildResult = "skipped"
if ($ForceBuild -or (Test-BuildRequired $executablePath $sourceRoots)) {
    & dotnet build $projectPath --configuration $Configuration --no-restore --nologo --verbosity quiet
    if ($LASTEXITCODE -ne 0) {
        throw "Ghostagram build failed. The fast start path does not restore packages; run dotnet restore separately if assets are missing."
    }
    $buildResult = "built"
}

if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf)) {
    throw "Ghostagram app host was not found at $executablePath after the build check."
}

$startInfo = [Diagnostics.ProcessStartInfo]::new()
$startInfo.FileName = $executablePath
$startInfo.Arguments = "--environment Development --urls `"$baseUrl`""
$startInfo.WorkingDirectory = $projectDirectory
$startInfo.UseShellExecute = $true
$startInfo.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
$startInfo.ErrorDialog = $false
$process = [Diagnostics.Process]::Start($startInfo)
if ($null -eq $process) {
    throw "Ghostagram process launch returned no process handle."
}

$processStartTimeTicks = $process.StartTime.ToUniversalTime().Ticks
$state = [ordered]@{
    schemaVersion = 1
    owner = "ghostagram-start-server"
    repositoryRoot = $repositoryRoot
    projectPath = $projectPath
    url = $baseUrl
    healthUri = $healthUrl
    pid = $process.Id
    processStartTimeUtcTicks = $processStartTimeTicks
    executablePath = $executablePath
    configuration = $Configuration
    status = "starting"
}
Write-State $runtime.State $state

$deadline = [DateTime]::UtcNow.AddSeconds($ReadinessTimeoutSeconds)
while ([DateTime]::UtcNow -lt $deadline) {
    if ($process.HasExited) {
        break
    }
    if (Test-Health $healthUrl) {
        $state.status = "ready"
        Write-State $runtime.State $state
        $startedAt.Stop()
        [pscustomobject]@{
            status = "started"
            healthy = $true
            managed = $true
            pid = $process.Id
            url = $baseUrl
            healthUri = $healthUrl
            build = $buildResult
            elapsedMs = $startedAt.ElapsedMilliseconds
            stateFile = $runtime.State
        } | ConvertTo-Json -Compress
        exit 0
    }
    Start-Sleep -Milliseconds 200
    $process.Refresh()
}

if (-not $process.HasExited) {
    Stop-Process -Id $process.Id -ErrorAction SilentlyContinue
}
Remove-Item -LiteralPath $runtime.State -ErrorAction SilentlyContinue
throw "Ghostagram did not become healthy within $ReadinessTimeoutSeconds seconds."
