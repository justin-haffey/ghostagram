param(
    [Parameter(Position = 0)]
    [string] $AgentName,

    [string] $Collection,
    [string] $Source
)

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
            error = 'Collection values must be relative subdirectories under .codex/agents.'
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
                error = 'Collection values cannot contain "." or ".." path segments.'
            }
        }

        if ($trimmed -match '[<>:"|?*]') {
            return [pscustomobject]@{
                normalized = $null
                error = 'Collection values contain characters that are not valid in a directory name.'
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

function Resolve-AgentFiles {
    param(
        [string] $SearchRoot,
        [string] $AgentIdentifier
    )

    $identifierSlug = Get-AgentSlug -Value $AgentIdentifier

    return @(Get-ChildItem -LiteralPath $SearchRoot -Recurse -File -Filter *.toml | Where-Object {
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
$targetCollectionInfo = Normalize-CollectionPath -Value $Collection
$sourceCollectionInfo = Normalize-CollectionPath -Value $Source
$targetCollection = $targetCollectionInfo.normalized
$sourceCollection = $sourceCollectionInfo.normalized
$sourceSearchRoot = if ($sourceCollection) { Join-Path $agentsRoot $sourceCollection } else { $agentsRoot }
$sourceMatches = if ($AgentName -and -not $sourceCollectionInfo.error -and (Test-Path -LiteralPath $sourceSearchRoot)) {
    Resolve-AgentFiles -SearchRoot $sourceSearchRoot -AgentIdentifier $AgentName
} else {
    @()
}
$targetDirectory = if ($targetCollection) { Join-Path $agentsRoot $targetCollection } else { $null }
$collectionDirectoryCreated = $false

if (-not $targetCollectionInfo.error -and $targetDirectory -and -not (Test-Path -LiteralPath $targetDirectory)) {
    New-Item -ItemType Directory -Path $targetDirectory -Force | Out-Null
    $collectionDirectoryCreated = $true
}

$targetFile = if ($sourceMatches.Count -eq 1 -and $targetDirectory) {
    Join-Path $targetDirectory $sourceMatches[0].Name
} else {
    $null
}
$sourceCollectionValue = if ($sourceMatches.Count -eq 1) {
    $relativeCollection = Get-RelativePath -BasePath $agentsRoot -TargetPath $sourceMatches[0].Directory.FullName
    if ([string]::IsNullOrWhiteSpace($relativeCollection)) { $null } else { $relativeCollection }
} else {
    $null
}

$referenceTerms = @()
if ($sourceMatches.Count -eq 1) {
    $referenceTerms += (Get-RelativePath -BasePath $projectRoot -TargetPath $sourceMatches[0].FullName)
    if ($sourceCollectionValue) {
        $referenceTerms += $sourceCollectionValue
    }
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
if (-not $Collection) {
    $notes.Add('Provide -Collection with the destination collection directory.') | Out-Null
}
if ($targetCollectionInfo.error) {
    $notes.Add($targetCollectionInfo.error) | Out-Null
}
if ($Source -and $sourceCollectionInfo.error) {
    $notes.Add("Source collection is invalid: $($sourceCollectionInfo.error)") | Out-Null
}
if ($Source -and -not $sourceCollectionInfo.error -and -not (Test-Path -LiteralPath $sourceSearchRoot)) {
    $notes.Add('The requested source collection does not exist.') | Out-Null
}
if ($sourceMatches.Count -eq 0 -and $AgentName -and -not $targetCollectionInfo.error -and -not ($Source -and -not (Test-Path -LiteralPath $sourceSearchRoot))) {
    $notes.Add('No agent matched the supplied identifier.') | Out-Null
}
if ($sourceMatches.Count -gt 1) {
    $notes.Add('Multiple agents matched the supplied identifier; disambiguation is required before moving.') | Out-Null
}
if ($targetFile -and $sourceMatches.Count -eq 1 -and ($sourceMatches[0].FullName -eq $targetFile)) {
    $notes.Add('The agent is already in the requested collection.') | Out-Null
}
if ($targetFile -and (Test-Path -LiteralPath $targetFile) -and ($sourceMatches[0].FullName -ne $targetFile)) {
    $notes.Add('The destination .toml path already exists.') | Out-Null
}

[pscustomobject]@{
    command = 'move-agent'
    sourceMatches = @($sourceMatches | ForEach-Object {
        [pscustomobject]@{
            file = $_.FullName
            relativeFile = Get-RelativePath -BasePath $projectRoot -TargetPath $_.FullName
            slug = $_.BaseName
            name = Get-AgentDefinitionName -FilePath $_.FullName
        }
    })
    sourceAgent = if ($sourceMatches.Count -eq 1) {
        [pscustomobject]@{
            file = $sourceMatches[0].FullName
            relativeFile = Get-RelativePath -BasePath $projectRoot -TargetPath $sourceMatches[0].FullName
            slug = $sourceMatches[0].BaseName
            name = Get-AgentDefinitionName -FilePath $sourceMatches[0].FullName
            directory = $sourceMatches[0].Directory.FullName
            collection = $sourceCollectionValue
        }
    } else {
        $null
    }
    targetCollection = $targetCollection
    targetDirectory = $targetDirectory
    collectionDirectoryCreated = $collectionDirectoryCreated
    targetAgent = if ($targetFile) {
        [pscustomobject]@{
            file = $targetFile
            relativeFile = Get-RelativePath -BasePath $projectRoot -TargetPath $targetFile
        }
    } else {
        $null
    }
    referenceSearchTerms = @($referenceTerms | Select-Object -Unique)
    referenceFiles = @($referenceFiles | ForEach-Object { Get-RelativePath -BasePath $projectRoot -TargetPath $_ })
    notes = @($notes)
} | ConvertTo-Json -Depth 6
