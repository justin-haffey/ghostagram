param(
    [string] $Research,
    [string] $Collection,
    [string] $AgentName,
    [string] $PrimaryPurpose,

    [ValidateSet('read-heavy', 'write-heavy', 'mixed')]
    [string] $WorkStyle,

    [string[]] $MainTasks,
    [string[]] $NonGoals,

    [ValidateSet('speed-first', 'balanced', 'deep-reasoning')]
    [string] $PreferredModelBehavior,

    [ValidateSet('inherit', 'read-only', 'workspace-write', 'danger-full-access')]
    [string] $SandboxPreference,

    [string[]] $ToolsOrIntegrations,
    [string[]] $SkillsNeeded,
    [string[]] $NicknameCandidates,
    [string] $ExtraConstraints
)

function Split-ListValues {
    param(
        [string[]] $Values
    )

    $items = [System.Collections.Generic.List[string]]::new()

    foreach ($value in $Values) {
        if ([string]::IsNullOrWhiteSpace($value)) {
            continue
        }

        foreach ($part in ($value -split '\r?\n|;|,')) {
            $trimmed = $part.Trim()
            if ($trimmed) {
                $items.Add($trimmed) | Out-Null
            }
        }
    }

    return @($items | Select-Object -Unique)
}

function Normalize-OptionalList {
    param(
        [string[]] $Values
    )

    $items = [string[]] (Split-ListValues -Values $Values)
    if ($items.Count -eq 0) {
        return $null
    }

    if ($items.Count -eq 1 -and $items[0].ToLowerInvariant() -eq 'none') {
        return 'none'
    }

    return $items
}

function To-JsonFriendlyValue {
    param(
        $Value
    )

    if ($null -eq $Value) {
        return $null
    }

    if ($Value -is [string]) {
        return $Value
    }

    if ($Value -is [System.Array]) {
        $list = [System.Collections.ArrayList]::new()
        foreach ($item in $Value) {
            $list.Add($item) | Out-Null
        }
        return $list
    }

    return $Value
}

function Get-AgentSlug {
    param(
        [string] $Name
    )

    if ([string]::IsNullOrWhiteSpace($Name)) {
        return $null
    }

    $slug = $Name.ToLowerInvariant()
    $slug = [System.Text.RegularExpressions.Regex]::Replace($slug, '[^a-z0-9]+', '-')
    $slug = [System.Text.RegularExpressions.Regex]::Replace($slug, '-{2,}', '-')
    $slug = $slug.Trim('-')

    if ([string]::IsNullOrWhiteSpace($slug)) {
        return $null
    }

    return $slug
}

function Normalize-CollectionPath {
    param(
        [string] $Value
    )

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return [pscustomobject]@{
            normalized = $null
            error = $null
        }
    }

    if ([System.IO.Path]::IsPathRooted($Value)) {
        return [pscustomobject]@{
            normalized = $null
            error = 'Collection must be a relative subdirectory under .codex/agents.'
        }
    }

    $segments = $Value -split '[\\/]+'
    $normalizedSegments = [System.Collections.Generic.List[string]]::new()

    foreach ($segment in $segments) {
        $trimmed = $segment.Trim()

        if (-not $trimmed) {
            continue
        }

        if ($trimmed -in @('.', '..')) {
            return [pscustomobject]@{
                normalized = $null
                error = 'Collection cannot contain "." or ".." path segments.'
            }
        }

        if ($trimmed -match '[<>:"|?*]') {
            return [pscustomobject]@{
                normalized = $null
                error = 'Collection contains characters that are not valid in a directory name.'
            }
        }

        $normalizedSegments.Add($trimmed) | Out-Null
    }

    if ($normalizedSegments.Count -eq 0) {
        return [pscustomobject]@{
            normalized = $null
            error = 'Collection must include at least one directory name.'
        }
    }

    return [pscustomobject]@{
        normalized = ($normalizedSegments -join '\')
        error = $null
    }
}

$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$codexRoot = Join-Path $projectRoot '.codex'
$agentsRoot = Join-Path $codexRoot 'agents'
$promptSource = Join-Path $codexRoot 'prompts\create-agent.prompt.md'
$metaPromptPath = Join-Path $codexRoot 'meta\meta-create-agent.prompt.md'
$researchMode = -not [string]::IsNullOrWhiteSpace($Research)
$collectionInfo = Normalize-CollectionPath -Value $Collection
$normalizedCollection = $collectionInfo.normalized
$collectionError = $collectionInfo.error
$agentSlug = Get-AgentSlug -Name $AgentName
$targetAgentDirectory = if ($normalizedCollection) { Join-Path $agentsRoot $normalizedCollection } else { $agentsRoot }
$targetAgentFile = if ($agentSlug) { Join-Path $targetAgentDirectory "$agentSlug.toml" } else { $null }
$collectionDirectoryCreated = $false

if (-not $collectionError -and $normalizedCollection -and -not (Test-Path -LiteralPath $targetAgentDirectory)) {
    New-Item -ItemType Directory -Path $targetAgentDirectory -Force | Out-Null
    $collectionDirectoryCreated = $true
}

$normalizedMainTasks = [string[]] (Split-ListValues -Values $MainTasks)
$normalizedNonGoals = [string[]] (Split-ListValues -Values $NonGoals)
$normalizedTools = Normalize-OptionalList -Values $ToolsOrIntegrations
$normalizedSkills = Normalize-OptionalList -Values $SkillsNeeded
$normalizedNicknames = [string[]] (Split-ListValues -Values $NicknameCandidates)

$requiredFields = if ($researchMode) {
    [ordered]@{}
} else {
    [ordered]@{
        agentName = $AgentName
        primaryPurpose = $PrimaryPurpose
        workStyle = $WorkStyle
        mainTasks = $normalizedMainTasks
        nonGoals = $normalizedNonGoals
        preferredModelBehavior = $PreferredModelBehavior
        sandboxPreference = $SandboxPreference
        toolsOrIntegrations = $normalizedTools
        skillsNeeded = $normalizedSkills
    }
}

$missingRequired = [System.Collections.Generic.List[string]]::new()

foreach ($entry in $requiredFields.GetEnumerator()) {
    $value = $entry.Value
    $isMissing =
        $null -eq $value -or
        ($value -is [string] -and [string]::IsNullOrWhiteSpace($value)) -or
        ($value -is [System.Array] -and $value.Count -eq 0)

    if ($isMissing) {
        $missingRequired.Add($entry.Key) | Out-Null
    }
}

$notes = [System.Collections.Generic.List[string]]::new()
if (-not (Test-Path -LiteralPath $promptSource)) {
    $notes.Add('The source prompt file was not found at .codex/prompts/create-agent.prompt.md.') | Out-Null
}
if (-not (Test-Path -LiteralPath $metaPromptPath)) {
    $notes.Add('The referenced meta prompt .codex/meta/meta-create-agent.prompt.md does not exist locally; use local agent examples as the fallback format reference.') | Out-Null
}
if ($collectionError) {
    $notes.Add($collectionError) | Out-Null
}
if (-not $agentSlug -and $AgentName) {
    $notes.Add('The agent name could not be converted into a stable file slug; verify the provided name.') | Out-Null
}
if ($researchMode -and -not $AgentName) {
    $notes.Add('Research mode is active without an explicit agent name; finalize the file slug after research determines the canonical agent name.') | Out-Null
}

[pscustomobject]@{
    command = 'new-agent'
    inputMode = if ($researchMode) { 'research' } else { 'structured' }
    promptSource = $promptSource
    metaPromptPath = $metaPromptPath
    metaPromptExists = Test-Path -LiteralPath $metaPromptPath
    targetAgentDirectory = $targetAgentDirectory
    targetAgentFile = $targetAgentFile
    collectionDirectoryCreated = $collectionDirectoryCreated
    missingRequired = @($missingRequired)
    normalizedInput = [pscustomobject] [ordered]@{
        research = if ([string]::IsNullOrWhiteSpace($Research)) { $null } else { $Research }
        collection = $normalizedCollection
        agentName = $AgentName
        agentSlug = $agentSlug
        primaryPurpose = $PrimaryPurpose
        workStyle = $WorkStyle
        mainTasks = To-JsonFriendlyValue -Value $normalizedMainTasks
        nonGoals = To-JsonFriendlyValue -Value $normalizedNonGoals
        preferredModelBehavior = $PreferredModelBehavior
        sandboxPreference = $SandboxPreference
        toolsOrIntegrations = To-JsonFriendlyValue -Value $normalizedTools
        skillsNeeded = To-JsonFriendlyValue -Value $normalizedSkills
        nicknameCandidates = To-JsonFriendlyValue -Value $normalizedNicknames
        extraConstraints = if ([string]::IsNullOrWhiteSpace($ExtraConstraints)) { $null } else { $ExtraConstraints }
    }
    notes = @($notes)
} | ConvertTo-Json -Depth 6
