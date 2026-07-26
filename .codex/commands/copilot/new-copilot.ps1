param(
    [Parameter(Position = 0)]
    [string] $Brief,

    [string] $Name,

    [string] $Audience,

    [string] $Domain,

    [string] $Constraints,

    [switch] $Mas,

    [switch] $WhatIf,

    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]] $RemainingBrief
)

$briefParts = [System.Collections.Generic.List[string]]::new()
if (-not [string]::IsNullOrWhiteSpace($Brief)) {
    $briefParts.Add($Brief.Trim()) | Out-Null
}
foreach ($part in $RemainingBrief) {
    if (-not [string]::IsNullOrWhiteSpace($part)) {
        $briefParts.Add($part.Trim()) | Out-Null
    }
}

$normalizedBrief = ($briefParts -join ' ').Trim()
$normalizedName = if ([string]::IsNullOrWhiteSpace($Name)) { $null } else { $Name.Trim() }
$normalizedAudience = if ([string]::IsNullOrWhiteSpace($Audience)) { $null } else { $Audience.Trim() }
$normalizedDomain = if ([string]::IsNullOrWhiteSpace($Domain)) { $null } else { $Domain.Trim() }
$normalizedConstraints = if ([string]::IsNullOrWhiteSpace($Constraints)) { $null } else { $Constraints.Trim() }

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..\..')
$outputRoot = Join-Path $repoRoot 'src\agents'
$workflowMode = if ($Mas) { 'multi-agent' } else { 'single-agent' }
$workflowFile = Join-Path $repoRoot ".codex\agents\copilot\templates\$workflowMode.workflow.md"

$notes = [System.Collections.Generic.List[string]]::new()
if (-not $normalizedBrief) {
    $notes.Add('Provide a brief so the copilot-orchestrator has a concrete objective.') | Out-Null
}
if ($WhatIf) {
    $notes.Add('WhatIf is active: resolve workflow and handoff only; do not generate output.') | Out-Null
}

$handoffLines = [System.Collections.Generic.List[string]]::new()
$handoffLines.Add("Workflow: $workflowMode") | Out-Null
$handoffLines.Add("Workflow file: $workflowFile") | Out-Null
$handoffLines.Add("Output root: $outputRoot") | Out-Null
if ($normalizedBrief) {
    $handoffLines.Add("Brief: $normalizedBrief") | Out-Null
}
if ($normalizedName) {
    $handoffLines.Add("Name: $normalizedName") | Out-Null
}
if ($normalizedAudience) {
    $handoffLines.Add("Audience: $normalizedAudience") | Out-Null
}
if ($normalizedDomain) {
    $handoffLines.Add("Domain: $normalizedDomain") | Out-Null
}
if ($normalizedConstraints) {
    $handoffLines.Add("Constraints: $normalizedConstraints") | Out-Null
}
if ($WhatIf) {
    $handoffLines.Add('WhatIf: true; resolve only, do not generate output.') | Out-Null
} else {
    $handoffLines.Add('WhatIf: false; run the selected Copilot workflow.') | Out-Null
}
$handoffLines.Add('Follow the Copilot collection override and the templates in .codex/agents/copilot/templates/.') | Out-Null

[pscustomobject]@{
    command = 'new-copilot'
    agent = 'copilot-orchestrator'
    workflow = $workflowMode
    workflowFile = $workflowFile
    brief = if ([string]::IsNullOrWhiteSpace($normalizedBrief)) { $null } else { $normalizedBrief }
    name = $normalizedName
    audience = $normalizedAudience
    domain = $normalizedDomain
    constraints = $normalizedConstraints
    outputRoot = $outputRoot
    whatIf = $WhatIf.IsPresent
    handoffMessage = ($handoffLines -join "`n")
    notes = @($notes)
} | ConvertTo-Json -Depth 5
