param(
    [switch] $Params,

    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]] $Args
)

$filter = ($Args -join ' ').Trim()
$commandsRoot = Split-Path -Parent $PSScriptRoot
$registryPath = Join-Path $commandsRoot 'commands.json'

if (-not (Test-Path -LiteralPath $registryPath)) {
    throw "Command registry not found at $registryPath"
}

$registry = Get-Content -Raw -LiteralPath $registryPath | ConvertFrom-Json
$commands = @($registry.commands)

if ($filter) {
    $commands = $commands | Where-Object {
        $_.category -like "*$filter*" -or
        $_.name -like "*$filter*" -or
        $_.description -like "*$filter*"
    }
}

if (-not $commands -or $commands.Count -eq 0) {
    Write-Output "No commands matched '$filter'."
    return
}

$lines = [System.Collections.Generic.List[string]]::new()
$grouped = $commands | Sort-Object category, name | Group-Object category

foreach ($group in $grouped) {
    $lines.Add("$($group.Name):") | Out-Null
    foreach ($command in ($group.Group | Sort-Object name)) {
        $lines.Add("  $($command.slash) - $($command.description)") | Out-Null
        if ($Params -and $command.parameters) {
            foreach ($parameter in $command.parameters) {
                $lines.Add("    $($parameter.name): $($parameter.description)") | Out-Null
                foreach ($example in @($parameter.examples)) {
                    if (-not [string]::IsNullOrWhiteSpace($example)) {
                        $lines.Add("      example: $example") | Out-Null
                    }
                }
            }
        }
        foreach ($example in @($command.examples)) {
            if (-not [string]::IsNullOrWhiteSpace($example)) {
                $lines.Add("    example: $example") | Out-Null
            }
        }
    }
}

$lines -join [Environment]::NewLine
