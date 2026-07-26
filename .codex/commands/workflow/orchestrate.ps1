param(
    [switch] $Plan,
    [string] $Prompt,

    [Parameter(Position = 0)]
    [string] $Context,

    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]] $RemainingContext
)

$contextParts = [System.Collections.Generic.List[string]]::new()
if (-not [string]::IsNullOrWhiteSpace($Context)) {
    $contextParts.Add($Context.Trim()) | Out-Null
}
foreach ($part in $RemainingContext) {
    if (-not [string]::IsNullOrWhiteSpace($part)) {
        $contextParts.Add($part.Trim()) | Out-Null
    }
}

$normalizedContext = ($contextParts -join ' ').Trim()
$normalizedPrompt = if ([string]::IsNullOrWhiteSpace($Prompt)) { $null } else { $Prompt.Trim() }
$mode = if ($Plan) { 'plan' } else { 'execute' }
$objective = if ($normalizedPrompt) { $normalizedPrompt } elseif ($normalizedContext) { $normalizedContext } else { $null }

$notes = [System.Collections.Generic.List[string]]::new()
if (-not $objective) {
    $notes.Add('Provide either <context> or -Prompt so the dev-orchestrator has a concrete objective.') | Out-Null
}
if ($Plan) {
    $notes.Add('Plan mode is active: the dev-orchestrator must not write code, edit files, or execute implementation work.') | Out-Null
}

$handoffLines = [System.Collections.Generic.List[string]]::new()
$handoffLines.Add("Mode: $mode") | Out-Null

if ($normalizedPrompt) {
    $handoffLines.Add("User prompt: $normalizedPrompt") | Out-Null
}

if ($normalizedContext) {
    $handoffLines.Add("Context: $normalizedContext") | Out-Null
}

if ($Plan) {
    $handoffLines.Add('Constraints: planning only; no code changes, no file edits, no implementation.') | Out-Null
} else {
    $handoffLines.Add('Constraints: orchestrate the work through the normal dev-orchestrator flow.') | Out-Null
}

$handoffLines.Add('Use the current workspace context and follow the project routing guidance in AGENTS.md.') | Out-Null

[pscustomobject]@{
    command = 'orchestrate'
    agent = 'dev-orchestrator'
    mode = $mode
    context = if ([string]::IsNullOrWhiteSpace($normalizedContext)) { $null } else { $normalizedContext }
    prompt = $normalizedPrompt
    objective = $objective
    handoffMessage = ($handoffLines -join "`n")
    notes = @($notes)
} | ConvertTo-Json -Depth 5
