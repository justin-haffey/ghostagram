param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]] $CommandArgs
)

function Parse-CommandArguments {
    param(
        [string[]] $ArgsToParse
    )

    $samplePaths = [System.Collections.Generic.List[string]]::new()
    $contextPath = $null
    $outFile = $null
    $templateOnly = $false

    for ($index = 0; $index -lt $ArgsToParse.Count; $index++) {
        $token = $ArgsToParse[$index]

        switch -Regex ($token) {
            '^-TemplateOnly$' {
                $templateOnly = $true
                continue
            }
            '^-ContextPath$' {
                if ($index + 1 -ge $ArgsToParse.Count) {
                    throw 'Missing value for -ContextPath.'
                }

                $index++
                $contextPath = $ArgsToParse[$index]
                continue
            }
            '^-OutFile$' {
                if ($index + 1 -ge $ArgsToParse.Count) {
                    throw 'Missing value for -OutFile.'
                }

                $index++
                $outFile = $ArgsToParse[$index]
                continue
            }
            '^-.*$' {
                throw "Unknown argument: $token"
            }
            default {
                $samplePaths.Add($token) | Out-Null
                continue
            }
        }
    }

    return [pscustomobject]@{
        samplePaths = @($samplePaths)
        contextPath = $contextPath
        outFile = $outFile
        templateOnly = $templateOnly
    }
}

function Resolve-RequiredPaths {
    param(
        [string[]] $Paths,
        [string] $ParameterName
    )

    if (-not $Paths -or $Paths.Count -eq 0) {
        throw 'Writing samples are required. Provide one or more local sample file paths, or use attached or uploaded samples with the command.'
    }

    $resolvedPaths = [System.Collections.Generic.List[string]]::new()

    foreach ($path in $Paths) {
        if ([string]::IsNullOrWhiteSpace($path)) {
            continue
        }

        try {
            $resolvedPath = (Resolve-Path -LiteralPath $path -ErrorAction Stop).Path
            $resolvedPaths.Add($resolvedPath) | Out-Null
        } catch {
            throw "Could not resolve $ParameterName path: $path"
        }
    }

    if ($resolvedPaths.Count -eq 0) {
        throw 'Writing samples are required. Provide one or more local sample file paths, or use attached or uploaded samples with the command.'
    }

    return @($resolvedPaths)
}

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

function Get-WordEstimate {
    param(
        [string] $Text
    )

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return 0
    }

    return ([regex]::Matches($Text.Trim(), '\S+')).Count
}

function Get-SampleSections {
    param(
        [string[]] $ResolvedPaths
    )

    $sections = [System.Collections.Generic.List[string]]::new()
    $manifest = [System.Collections.Generic.List[object]]::new()
    $index = 0

    foreach ($resolvedPath in $ResolvedPaths) {
        $index++
        $content = [System.IO.File]::ReadAllText($resolvedPath)
        $name = [System.IO.Path]::GetFileName($resolvedPath)
        $wordEstimate = Get-WordEstimate -Text $content

        $sections.Add("[SAMPLE $index - $name]") | Out-Null
        $sections.Add($content) | Out-Null

        $manifest.Add([pscustomobject]@{
            index = $index
            name = $name
            path = $resolvedPath
            wordEstimate = $wordEstimate
            characterCount = $content.Length
        }) | Out-Null
    }

    return [pscustomobject]@{
        combinedText = ($sections -join ([Environment]::NewLine + [Environment]::NewLine))
        manifest = @($manifest)
    }
}

$parsedArguments = Parse-CommandArguments -ArgsToParse $CommandArgs
$templateMode = $parsedArguments.templateOnly
$resolvedSamplePaths = @()
$sampleData = $null

if (-not $templateMode) {
    $resolvedSamplePaths = Resolve-RequiredPaths -Paths $parsedArguments.samplePaths -ParameterName 'SamplePaths'
    $sampleData = Get-SampleSections -ResolvedPaths $resolvedSamplePaths
}

$resolvedContextPath = Resolve-OptionalPath -Path $parsedArguments.contextPath -ParameterName 'ContextPath'

$samplesBlock = if ($templateMode) {
    '[Inject parsed writing samples here. If there are multiple samples, keep each sample labeled by source name and separated clearly.]'
} else {
    $sampleData.combinedText
}

$sampleManifest = if ($templateMode) {
    @()
} else {
    $sampleData.manifest
}

$contextBlock = if ($templateMode) {
    '(Optional) Intended output domain, audience, purpose, and taboo constraints.'
} else {
    Get-OptionalFileText -ResolvedPath $resolvedContextPath -Fallback '(Optional) Intended output domain, audience, purpose, and taboo constraints.'
}

$prompt = @"
SYSTEM:
You are a computational stylistics analyst and editorial style engineer.
Your job is to extract a reusable, testable "Style Card" from writing samples.
You do NOT imitate or quote long passages. You generalize patterns.
You prioritize: (1) high-frequency structural signals (function words, syntax, cadence),
(2) discourse organization, and (3) lexicon choices, in that order.

USER:
TASK: Build a STYLE_CARD that captures the essence of an author's writing style.

INPUTS:
- Author writing samples (raw text), delimited by <SAMPLES> ... </SAMPLES>
- Optional: Target domain, audience, and purpose constraints, delimited by <CONTEXT> ... </CONTEXT>

<SAMPLES>
$samplesBlock
</SAMPLES>

<CONTEXT>
$contextBlock
</CONTEXT>

REQUIREMENTS:
1) Separate STYLE from CONTENT.
   - Identify content-bound elements such as topic jargon, named entities, and recurring anecdotes.
   - Mark those elements as NON-STYLE so they are not treated as required in new writing.
2) Identify STYLE INVARIANTS that should hold across topics:
   - Function-word tendencies: pronoun style, articles density, auxiliaries, prepositions, formality markers, hedge and booster usage.
   - Sentence architecture: typical length bands, clause depth, coordination vs subordination, fragments, rhetorical questions, parentheticals.
   - Cadence: variation in sentence length and sentence openings; beat patterns such as short-short-long or long with a periodic punchline.
   - Punctuation fingerprint: commas vs dashes vs semicolons; colon usage; list habits.
   - Cohesion habits: favorite transitions, paragraphing rhythm, and signposting style.
3) Identify STYLE PREFERENCES:
   - Figurative language density, humor dryness, analogy style, level of abstraction, and emotional tone bounds.
4) Produce EVIDENCE WITHOUT COPYING:
   - Provide only micro-examples with a maximum of 12 words each.
   - Prefer invented minimal pairs that demonstrate patterns without reusing distinctive phrases.
5) Include ANTI-PLAGIARISM GUARDRAILS:
   - Provide a "Do Not Reuse" list covering distinctive phrases, mottos, catch-phrases, unusual metaphors, and named anecdotes.
6) Provide an EVALUATION CHECKLIST aligned to:
   - Style match
   - Content preservation for future rewrites
   - Fluency and readability

OUTPUT FORMAT (exactly this structure):
A) STYLE_DNA (10 lines max): crisp description of the voice and its defining moves.
B) STYLE_CARD (YAML or JSON-like, but human-readable) with fields:
   - context_defaults
   - voice_persona
   - tone_range
   - diction
   - syntax
   - cadence
   - cohesion_and_structure
   - rhetoric_and_devices
   - formatting_conventions
   - banned_reuse_list
   - do_list
   - dont_list
   - evaluation_checklist
C) CONFIDENCE NOTES:
   - What is strongly supported vs uncertain due to limited sample size or mixed genres.
"@

$savedTo = $null
if ($parsedArguments.outFile) {
    $resolvedOutFile = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($parsedArguments.outFile)
    Set-Content -LiteralPath $resolvedOutFile -Value $prompt -Encoding utf8
    $savedTo = $resolvedOutFile
}

[pscustomobject]@{
    command = 'capture-style'
    mode = if ($templateMode) { 'template' } else { 'hydrated' }
    samplePaths = $resolvedSamplePaths
    sampleCount = $resolvedSamplePaths.Count
    sampleManifest = $sampleManifest
    contextPath = $resolvedContextPath
    outputFile = $savedTo
    prompt = $prompt
    notes = @(
        'Writing samples are required unless you are explicitly generating a blank template.',
        'Prioritize frequent structural signals over signature phrases.',
        'Treat named anecdotes and catch-phrases as non-style unless the model explicitly marks them reusable.'
    )
} | ConvertTo-Json -Depth 6
