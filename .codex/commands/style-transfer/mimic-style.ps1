param(
    [Parameter(Position = 0)]
    [string] $StyleCardPath,

    [Parameter(Position = 1)]
    [string] $SpecPath,

    [Parameter(Position = 2)]
    [string] $DraftPath,

    [string] $OutFile,

    [switch] $IncludeCriticTemplate
)

function Resolve-OptionalPath {
    param(
        [string] $Path,
        [string] $ParameterName
    )

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return $null
    }

    try {
        return (Resolve-Path -LiteralPath $Path -ErrorAction Stop).Path
    } catch {
        throw "Could not resolve $ParameterName path: $Path"
    }
}

function Get-OptionalFileText {
    param(
        [string] $ResolvedPath,
        [string] $Fallback
    )

    if (-not $ResolvedPath) {
        return $Fallback
    }

    return [System.IO.File]::ReadAllText($ResolvedPath)
}

$resolvedStyleCardPath = Resolve-OptionalPath -Path $StyleCardPath -ParameterName 'StyleCardPath'
$resolvedSpecPath = Resolve-OptionalPath -Path $SpecPath -ParameterName 'SpecPath'
$resolvedDraftPath = Resolve-OptionalPath -Path $DraftPath -ParameterName 'DraftPath'
$templateMode = -not $resolvedStyleCardPath -or -not $resolvedSpecPath

$styleCardBlock = Get-OptionalFileText -ResolvedPath $resolvedStyleCardPath -Fallback '[Paste the STYLE_CARD produced by the style capture prompt.]'
$specBlock = Get-OptionalFileText -ResolvedPath $resolvedSpecPath -Fallback @'
- Document type:
- Audience:
- Purpose:
- Length target (words or sections):
- Required points / outline:
- Constraints (must include / must avoid):
- Source material (if rewriting): paste text here; otherwise leave blank.
'@
$draftBlock = Get-OptionalFileText -ResolvedPath $resolvedDraftPath -Fallback '[Paste draft here.]'

$generationPrompt = @"
SYSTEM:
You are an expert writer. You will generate new, original text that follows a provided STYLE_CARD.
You must not copy phrases from the samples. You must preserve meaning requirements.
If there is any tension between requirements, prefer: (1) factual correctness, (2) safety,
(3) the user's constraints, then (4) style fidelity.

USER:
TASK: Write a new document in the style defined by STYLE_CARD.

INPUTS:
<STYLE_CARD>
$styleCardBlock
</STYLE_CARD>

<NEW_DOC_SPEC>
$specBlock
</NEW_DOC_SPEC>

GENERATION RULES:
1) Preserve content requirements exactly, including facts, required points, and disclaimers.
2) Apply style invariants from STYLE_CARD:
   - function-word stance
   - syntax profile
   - cadence profile
   - cohesion profile
   - punctuation habits
3) Apply style preferences within bounds, but do not overfit.
   - Avoid caricature. The goal is native, not parody.
4) Enforce banned_reuse_list strictly.
   - Do not reuse listed phrases or distinctive metaphors.
5) Produce 2 candidates if feasible:
   - Candidate A: maximum style fidelity within constraints.
   - Candidate B: slightly reduced stylistic intensity for broader audience readability.

OUTPUT:
- Candidate A
- Candidate B
- A short "Style Compliance Notes" section:
  * 5 bullets max describing what you intentionally did to match style, with no quotes from samples.
"@

$criticPrompt = $null
if ($IncludeCriticTemplate -or $resolvedDraftPath -or $templateMode) {
    $criticPrompt = @"
SYSTEM:
You are a style-transfer evaluator. You grade and revise text against:
(1) style match to STYLE_CARD, (2) content preservation vs spec or source, and (3) fluency.

USER:
INPUTS:
<STYLE_CARD>
$styleCardBlock
</STYLE_CARD>

<NEW_DOC_SPEC>
$specBlock
</NEW_DOC_SPEC>

<DRAFT>
$draftBlock
</DRAFT>

INSTRUCTIONS:
1) Score each dimension from 1-5:
   - Style match
   - Content preservation
   - Fluency
2) Provide a diagnostic:
   - 5 mismatches max, each mapped to a STYLE_CARD field such as cadence or syntax.
3) Revise the draft once:
   - Maintain meaning and constraints.
   - Increase style match using high-frequency signals such as function words, syntax, and cadence.
   - Do not add flashy signature phrases.
4) Confirm anti-copy compliance:
   - State whether any sentence risks copying.
   - If yes, rewrite those lines.

OUTPUT:
- Scores
- Diagnostics
- Revised Draft
- Anti-copy check result
"@
}

$savedTo = $null
if ($OutFile) {
    $resolvedOutFile = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutFile)
    $bundle = @(
        '### Generation Prompt'
        $generationPrompt
    )

    if ($criticPrompt) {
        $bundle += @(
            ''
            '### Critic Prompt'
            $criticPrompt
        )
    }

    Set-Content -LiteralPath $resolvedOutFile -Value ($bundle -join [Environment]::NewLine) -Encoding utf8
    $savedTo = $resolvedOutFile
}

[pscustomobject]@{
    command = 'mimic-style'
    mode = if ($templateMode) { 'template' } else { 'hydrated' }
    styleCardPath = $resolvedStyleCardPath
    specPath = $resolvedSpecPath
    draftPath = $resolvedDraftPath
    outputFile = $savedTo
    generationPrompt = $generationPrompt
    criticPrompt = $criticPrompt
    notes = @(
        'Prefer factual correctness and user constraints over style mimicry.',
        'Reproduce structural tendencies, not distinctive wording from source samples.'
    )
} | ConvertTo-Json -Depth 5
