[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$pluginRoot = Split-Path -Parent $PSScriptRoot
$repoRoot = (Resolve-Path (Join-Path $pluginRoot '..\..')).Path
$canonicalMcpUrl = 'http://127.0.0.1:5256/mcp'
$expectedTools = @(
    'describe_capabilities',
    'list_diagrams',
    'open_session',
    'create_diagram',
    'get_diagram',
    'apply_operations',
    'layout_diagram',
    'export_svg',
    'close_session'
)

$failures = [System.Collections.Generic.List[string]]::new()
$checks = 0

function Assert-PluginCondition {
    param(
        [Parameter(Mandatory)] [bool] $Condition,
        [Parameter(Mandatory)] [string] $Message
    )

    $script:checks++
    if (-not $Condition) {
        $script:failures.Add($Message)
    }
}

$manifestPath = Join-Path $pluginRoot '.codex-plugin\plugin.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
Assert-PluginCondition ($manifest.name -eq 'ghostagram') 'Plugin manifest name must be ghostagram.'
Assert-PluginCondition ([version]$manifest.version -ge [version]'1.0.2') 'Plugin manifest version must include the live-collaboration release.'

$mcpConfigPath = Join-Path $pluginRoot '.mcp.json'
$mcpConfig = Get-Content -LiteralPath $mcpConfigPath -Raw | ConvertFrom-Json
Assert-PluginCondition ($mcpConfig.mcpServers.ghostagram.url -eq $canonicalMcpUrl) ".mcp.json must use $canonicalMcpUrl."

$skillDirectories = Get-ChildItem -LiteralPath (Join-Path $pluginRoot 'skills') -Directory | Sort-Object Name
foreach ($skillDirectory in $skillDirectories) {
    $skillPath = Join-Path $skillDirectory.FullName 'SKILL.md'
    Assert-PluginCondition (Test-Path -LiteralPath $skillPath -PathType Leaf) "Missing SKILL.md for $($skillDirectory.Name)."
    if (-not (Test-Path -LiteralPath $skillPath -PathType Leaf)) {
        continue
    }

    $skillText = Get-Content -LiteralPath $skillPath -Raw
    $nameMatch = [regex]::Match($skillText, '(?m)^name:\s*([^\r\n]+)$')
    Assert-PluginCondition ($nameMatch.Success) "Skill $($skillDirectory.Name) has no frontmatter name."
    if ($nameMatch.Success) {
        Assert-PluginCondition ($nameMatch.Groups[1].Value.Trim() -eq $skillDirectory.Name) "Skill folder and frontmatter name differ for $($skillDirectory.Name)."
    }

    foreach ($contractMatch in [regex]::Matches($skillText, 'plugins/ghostagram/contracts/([A-Za-z0-9._-]+\.md)')) {
        $contractPath = Join-Path $pluginRoot ('contracts\' + $contractMatch.Groups[1].Value)
        Assert-PluginCondition (Test-Path -LiteralPath $contractPath -PathType Leaf) "Skill $($skillDirectory.Name) references missing contract $($contractMatch.Groups[1].Value)."
    }

    $agentMetadataPath = Join-Path $skillDirectory.FullName 'agents\openai.yaml'
    Assert-PluginCondition (Test-Path -LiteralPath $agentMetadataPath -PathType Leaf) "Missing agents/openai.yaml for $($skillDirectory.Name)."
    if (Test-Path -LiteralPath $agentMetadataPath -PathType Leaf) {
        $agentMetadata = Get-Content -LiteralPath $agentMetadataPath -Raw
        $declaresMcp = $agentMetadata -match '(?m)^\s*-\s+type:\s*"?mcp"?\s*$'
        if ($declaresMcp) {
            Assert-PluginCondition ($agentMetadata -match [regex]::Escape($canonicalMcpUrl)) "Skill $($skillDirectory.Name) uses a non-canonical MCP URL."
        }
    }
}

$agentPaths = @(
    (Join-Path $repoRoot '.codex\agents\gram\diagram-developer.toml')
    (Join-Path $repoRoot '.codex\agents\gram\diagram-developer2.toml')
)
foreach ($agentPath in $agentPaths) {
    $agentText = Get-Content -LiteralPath $agentPath -Raw
    Assert-PluginCondition ($agentText -match [regex]::Escape($canonicalMcpUrl)) "Agent $(Split-Path -Leaf $agentPath) uses a non-canonical MCP URL."
    foreach ($tool in $expectedTools) {
        Assert-PluginCondition ($agentText -match ('"' + [regex]::Escape($tool) + '"')) "Agent $(Split-Path -Leaf $agentPath) does not allow $tool."
    }
}

$schemaPath = Join-Path $repoRoot 'src\Ghostagram.Contracts\Schemas\ghostagram-authoring.v1.schema.json'
$schema = Get-Content -LiteralPath $schemaPath -Raw | ConvertFrom-Json
Assert-PluginCondition ($schema.'$schema' -eq 'https://json-schema.org/draft/2020-12/schema') 'Authoring schema must use JSON Schema 2020-12.'
Assert-PluginCondition ($null -ne $schema.'$defs'.operation) 'Authoring schema must define the operation union.'
Assert-PluginCondition ($null -ne $schema.'$defs'.diagramDocument) 'Authoring schema must define a diagram document.'

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Error $_ }
    throw "Ghostagram plugin validation failed with $($failures.Count) error(s)."
}

[pscustomobject]@{
    status = 'PASS'
    checks = $checks
    skills = $skillDirectories.Count
    tools = $expectedTools.Count
    mcpUrl = $canonicalMcpUrl
    schema = $schema.'$id'
} | ConvertTo-Json -Compress
