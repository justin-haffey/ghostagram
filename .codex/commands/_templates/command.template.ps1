param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]] $Args
)

$joinedArgs = ($Args -join ' ').Trim()

[pscustomobject]@{
    command = 'command-name'
    arguments = $joinedArgs
    note = 'Replace this template script with command-specific behavior.'
} | ConvertTo-Json -Depth 3
