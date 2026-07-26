param(
    [Parameter(Position = 0)]
    [string] $Target,

    [switch] $NoScript,

    [switch] $WhatIf
)

if (-not $Target) {
    throw 'Usage: new-command.ps1 <category/command-name> [-NoScript] [-WhatIf]'
}

$normalized = $Target.Replace('\', '/').Trim('/')
$segments = $normalized.Split('/', [System.StringSplitOptions]::RemoveEmptyEntries)

if ($segments.Count -ne 2) {
    throw 'Target must use the form <category/command-name>.'
}

$category = $segments[0].ToLowerInvariant()
$command = $segments[1].ToLowerInvariant()

if ($category.StartsWith('_')) {
    throw 'Category names that start with "_" are reserved for scaffolding folders.'
}

$commandsRoot = Split-Path -Parent $PSScriptRoot
$categoryPath = Join-Path $commandsRoot $category
$promptPath = Join-Path $categoryPath "$command.md"
$scriptPath = Join-Path $categoryPath "$command.ps1"
$registryPath = Join-Path $commandsRoot 'commands.json'

$promptTemplate = @"
---
description: Describe what /$command should do
argument-hint: Optional command arguments
script: ./$command.ps1
shell: powershell
---

# $command

Execute `/$command` with arguments: `$ARGUMENTS`

## Intent

Describe the user outcome this command should achieve.

## Execution

1. Inspect the relevant project context.
2. Run the paired script if it is needed.
3. Perform the requested workflow.
4. Validate the result.

## Output

- Summarize the result clearly.
"@

$scriptTemplate = @"
param(
    [Parameter(ValueFromRemainingArguments = `$true)]
    [string[]] `$Args
)

`$joinedArgs = (`$Args -join ' ').Trim()

[pscustomobject]@{
    command = '$command'
    arguments = `$joinedArgs
    note = 'Replace this scaffold with command-specific behavior.'
} | ConvertTo-Json -Depth 3
"@

$created = [System.Collections.Generic.List[string]]::new()
$skipped = [System.Collections.Generic.List[string]]::new()

if ($WhatIf) {
    [pscustomobject]@{
        category = $category
        command = $command
        prompt = $promptPath
        script = if ($NoScript) { $null } else { $scriptPath }
        registry = $registryPath
        whatIf = $true
    } | ConvertTo-Json -Depth 3
    return
}

New-Item -ItemType Directory -Path $categoryPath -Force | Out-Null

if (-not (Test-Path -LiteralPath $promptPath)) {
    Set-Content -LiteralPath $promptPath -Value $promptTemplate -Encoding utf8
    $created.Add($promptPath) | Out-Null
} else {
    $skipped.Add($promptPath) | Out-Null
}

if (-not $NoScript) {
    if (-not (Test-Path -LiteralPath $scriptPath)) {
        Set-Content -LiteralPath $scriptPath -Value $scriptTemplate -Encoding utf8
        $created.Add($scriptPath) | Out-Null
    } else {
        $skipped.Add($scriptPath) | Out-Null
    }
}

if (-not (Test-Path -LiteralPath $registryPath)) {
    $emptyRegistry = [ordered]@{
        commands = @()
    } | ConvertTo-Json -Depth 10
    Set-Content -LiteralPath $registryPath -Value $emptyRegistry -Encoding utf8
}

$registry = Get-Content -Raw -LiteralPath $registryPath | ConvertFrom-Json
$existingCommands = [System.Collections.Generic.List[object]]::new()
foreach ($existing in @($registry.commands)) {
    $existingCommands.Add($existing) | Out-Null
}

$existingEntry = $existingCommands | Where-Object {
    $_.category -eq $category -and $_.name -eq $command
} | Select-Object -First 1

if (-not $existingEntry) {
    $newEntry = [pscustomobject]@{
        category = $category
        name = $command
        slash = "/$command"
        description = "Describe what /$command should do."
        script = if ($NoScript) { $null } else { "./$category/$command.ps1" }
        examples = @("/$command")
        parameters = @(
            [pscustomobject]@{
                name = "[arguments]"
                description = "Optional command arguments."
                examples = @("argument-value")
            }
        )
    }
    $existingCommands.Add($newEntry) | Out-Null
} else {
    if (-not $NoScript -and -not $existingEntry.script) {
        $existingEntry.script = "./$category/$command.ps1"
    }
}

$sorted = $existingCommands | Sort-Object category, name
$registryObject = [pscustomobject]@{
    commands = @($sorted)
}
$registryObject | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $registryPath -Encoding utf8

[pscustomobject]@{
    category = $category
    command = $command
    created = $created
    skipped = $skipped
    scriptGenerated = -not $NoScript
    registry = $registryPath
} | ConvertTo-Json -Depth 5
