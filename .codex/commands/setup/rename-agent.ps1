param(
    [Parameter(Position = 0)]
    [string] $AgentName,

    [string] $Name
)

function Get-AgentSlug {
    param(
        [string] $Value
    )

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return $null
    }

    $slug = $Value.ToLowerInvariant()
    $slug = [System.Text.RegularExpressions.Regex]::Replace($slug, '[^a-z0-9]+', '-')
    $slug = [System.Text.RegularExpressions.Regex]::Replace($slug, '-{2,}', '-')
    $slug = $slug.Trim('-')

    if ([string]::IsNullOrWhiteSpace($slug)) {
        return $null
    }

    return $slug
}

function Get-RelativePath {
    param(
        [string] $BasePath,
        [string] $TargetPath
    )

    $resolvedBase = (Resolve-Path -LiteralPath $BasePath).Path
    $resolvedTarget = [System.IO.Path]::GetFullPath($TargetPath)

    if ((Test-Path -LiteralPath $resolvedBase -PathType Container) -and -not $resolvedBase.EndsWith('\')) {
        $resolvedBase = "$resolvedBase\"
    }

    if ((Test-Path -LiteralPath $resolvedTarget -PathType Container) -and -not $resolvedTarget.EndsWith('\')) {
        $resolvedTarget = "$resolvedTarget\"
    }

    $baseUri = [System.Uri] $resolvedBase
    $targetUri = [System.Uri] $resolvedTarget
    $relativeUri = $baseUri.MakeRelativeUri($targetUri)

    return [System.Uri]::UnescapeDataString($relativeUri.ToString()).TrimEnd('/')
}

function Get-AgentDefinitionName {
    param(
        [string] $FilePath
    )

    $match = Select-String -LiteralPath $FilePath -Pattern '^\s*name\s*=\s*"(.+)"\s*$' | Select-Object -First 1
    if ($match) {
        return $match.Matches[0].Groups[1].Value
    }

    return $null
}

function Resolve-AgentFiles {
    param(
        [string] $AgentsRoot,
        [string] $AgentIdentifier
    )

    $identifierSlug = Get-AgentSlug -Value $AgentIdentifier

    return @(Get-ChildItem -LiteralPath $AgentsRoot -Recurse -File -Filter *.toml | Where-Object {
        $definitionName = Get-AgentDefinitionName -FilePath $_.FullName
        $_.BaseName -ieq $AgentIdentifier -or
        ($identifierSlug -and $_.BaseName -ieq $identifierSlug) -or
        ($definitionName -and $definitionName -ieq $AgentIdentifier)
    })
}

function Find-ReferenceFiles {
    param(
        [string] $ProjectRoot,
        [string[]] $Terms
    )

    $rg = Get-Command rg -ErrorAction SilentlyContinue
    if (-not $rg) {
        return @()
    }

    $results = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)

    foreach ($term in ($Terms | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique)) {
        $matches = & $rg.Source -l --fixed-strings --glob "!**/.git/**" -- $term $ProjectRoot 2>$null
        foreach ($match in $matches) {
            if ($match) {
                [void] $results.Add((Resolve-Path -LiteralPath $match).Path)
            }
        }
    }

    return @($results)
}

$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$agentsRoot = Join-Path $projectRoot '.codex\agents'
$resolvedAgents = if ($AgentName) { Resolve-AgentFiles -AgentsRoot $agentsRoot -AgentIdentifier $AgentName } else { @() }
$oldName = if ($resolvedAgents.Count -eq 1) { Get-AgentDefinitionName -FilePath $resolvedAgents[0].FullName } else { $null }
$newSlug = Get-AgentSlug -Value $Name
$targetFile = if ($resolvedAgents.Count -eq 1 -and $newSlug) {
    Join-Path $resolvedAgents[0].Directory.FullName "$newSlug.toml"
} else {
    $null
}

$referenceTerms = @()
if ($resolvedAgents.Count -eq 1) {
    $referenceTerms += $resolvedAgents[0].BaseName
    $referenceTerms += (Get-RelativePath -BasePath $projectRoot -TargetPath $resolvedAgents[0].FullName)
}
if ($oldName) {
    $referenceTerms += $oldName
}

$referenceFiles = if ($referenceTerms.Count -gt 0) {
    Find-ReferenceFiles -ProjectRoot $projectRoot -Terms $referenceTerms
} else {
    @()
}

$notes = [System.Collections.Generic.List[string]]::new()
if (-not $AgentName) {
    $notes.Add('Provide the existing agent name as the first argument.') | Out-Null
}
if (-not $Name) {
    $notes.Add('Provide -Name with the new canonical agent name.') | Out-Null
}
if ($Name -and -not $newSlug) {
    $notes.Add('The new name could not be converted into a stable file slug.') | Out-Null
}
if ($resolvedAgents.Count -eq 0 -and $AgentName) {
    $notes.Add('No agent matched the supplied identifier.') | Out-Null
}
if ($resolvedAgents.Count -gt 1) {
    $notes.Add('Multiple agents matched the supplied identifier; disambiguation is required before renaming.') | Out-Null
}
if ($targetFile -and (Test-Path -LiteralPath $targetFile) -and ($resolvedAgents[0].FullName -ne $targetFile)) {
    $notes.Add('The target .toml path already exists.') | Out-Null
}

[pscustomobject]@{
    command = 'rename-agent'
    sourceMatches = @($resolvedAgents | ForEach-Object {
        [pscustomobject]@{
            file = $_.FullName
            relativeFile = Get-RelativePath -BasePath $projectRoot -TargetPath $_.FullName
            slug = $_.BaseName
            name = Get-AgentDefinitionName -FilePath $_.FullName
        }
    })
    sourceAgent = if ($resolvedAgents.Count -eq 1) {
        [pscustomobject]@{
            file = $resolvedAgents[0].FullName
            relativeFile = Get-RelativePath -BasePath $projectRoot -TargetPath $resolvedAgents[0].FullName
            slug = $resolvedAgents[0].BaseName
            name = $oldName
            directory = $resolvedAgents[0].Directory.FullName
        }
    } else {
        $null
    }
    targetAgent = if ($targetFile) {
        [pscustomobject]@{
            file = $targetFile
            relativeFile = Get-RelativePath -BasePath $projectRoot -TargetPath $targetFile
            slug = $newSlug
            name = $Name
        }
    } else {
        $null
    }
    referenceSearchTerms = @($referenceTerms | Select-Object -Unique)
    referenceFiles = @($referenceFiles | ForEach-Object { Get-RelativePath -BasePath $projectRoot -TargetPath $_ })
    notes = @($notes)
} | ConvertTo-Json -Depth 6
