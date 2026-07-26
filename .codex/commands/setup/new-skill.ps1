param(
    [Parameter(Position = 0)]
    [string] $SkillName,

    [switch] $NoMetadata,

    [switch] $NoReferences,

    [switch] $NoScripts,

    [switch] $WhatIf
)

if (-not $SkillName) {
    throw 'Usage: new-skill.ps1 <skill-name> [-NoMetadata] [-NoReferences] [-NoScripts] [-WhatIf]'
}

$slug =
    ($SkillName.Trim().ToLowerInvariant() -replace '[^a-z0-9\-]+', '-') `
    -replace '-{2,}', '-'
$slug = $slug.Trim('-')

if (-not $slug) {
    throw 'Skill name must contain at least one letter or number.'
}

if ($slug.StartsWith('_')) {
    throw 'Skill names that start with "_" are reserved for scaffolding folders.'
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..\..')
$skillsRoot = Join-Path $repoRoot '.codex\skills'
$skillRoot = Join-Path $skillsRoot $slug
$skillFile = Join-Path $skillRoot 'SKILL.md'
$metadataDir = Join-Path $skillRoot 'agents'
$metadataFile = Join-Path $metadataDir 'openai.yaml'
$referencesDir = Join-Path $skillRoot 'references'
$scriptsDir = Join-Path $skillRoot 'scripts'
$assetsDir = Join-Path $skillRoot 'assets'
$displayName = (($slug -split '-') | Where-Object { $_ } | ForEach-Object {
    $_.Substring(0, 1).ToUpperInvariant() + $_.Substring(1)
}) -join ' '

$skillTemplate = @"
---
name: $slug
description: Explain exactly when this skill should and should not trigger.
---

# Purpose

Describe the job this skill does and the outcome it should produce.

## When To Use

- Use when:
- Do not use when:

## Workflow

1. Inspect the minimum relevant project context.
2. Use only the tools and references needed for this task.
3. Produce the requested output and validate the result when possible.

## References

- Add optional reference files under `references/` and point to them here.
"@

$metadataTemplate = @"
interface:
  display_name: "$displayName"
  short_description: "Optional user-facing description"
policy:
  allow_implicit_invocation: true
dependencies:
  tools: []
"@

$created = [System.Collections.Generic.List[string]]::new()
$skipped = [System.Collections.Generic.List[string]]::new()

if ($WhatIf) {
    [pscustomobject]@{
        skill = $slug
        skillRoot = $skillRoot
        skillFile = $skillFile
        metadataFile = if ($NoMetadata) { $null } else { $metadataFile }
        referencesDir = if ($NoReferences) { $null } else { $referencesDir }
        scriptsDir = if ($NoScripts) { $null } else { $scriptsDir }
        assetsDir = $assetsDir
        whatIf = $true
    } | ConvertTo-Json -Depth 5
    return
}

foreach ($directory in @($skillsRoot, $skillRoot, $assetsDir)) {
    if (-not (Test-Path -LiteralPath $directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
        $created.Add($directory) | Out-Null
    } else {
        $skipped.Add($directory) | Out-Null
    }
}

if (-not $NoReferences) {
    if (-not (Test-Path -LiteralPath $referencesDir)) {
        New-Item -ItemType Directory -Path $referencesDir -Force | Out-Null
        $created.Add($referencesDir) | Out-Null
    } else {
        $skipped.Add($referencesDir) | Out-Null
    }
}

if (-not $NoScripts) {
    if (-not (Test-Path -LiteralPath $scriptsDir)) {
        New-Item -ItemType Directory -Path $scriptsDir -Force | Out-Null
        $created.Add($scriptsDir) | Out-Null
    } else {
        $skipped.Add($scriptsDir) | Out-Null
    }
}

if (-not (Test-Path -LiteralPath $skillFile)) {
    Set-Content -LiteralPath $skillFile -Value $skillTemplate -Encoding utf8
    $created.Add($skillFile) | Out-Null
} else {
    $skipped.Add($skillFile) | Out-Null
}

if (-not $NoMetadata) {
    if (-not (Test-Path -LiteralPath $metadataDir)) {
        New-Item -ItemType Directory -Path $metadataDir -Force | Out-Null
        $created.Add($metadataDir) | Out-Null
    } else {
        $skipped.Add($metadataDir) | Out-Null
    }

    if (-not (Test-Path -LiteralPath $metadataFile)) {
        Set-Content -LiteralPath $metadataFile -Value $metadataTemplate -Encoding utf8
        $created.Add($metadataFile) | Out-Null
    } else {
        $skipped.Add($metadataFile) | Out-Null
    }
}

[pscustomobject]@{
    skill = $slug
    created = $created
    skipped = $skipped
    metadataGenerated = -not $NoMetadata
    referencesGenerated = -not $NoReferences
    scriptsGenerated = -not $NoScripts
} | ConvertTo-Json -Depth 5
