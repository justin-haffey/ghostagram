---
description: Extract a reusable STYLE_CARD from required writing samples, including uploaded samples when present
argument-hint: <sample-file...> [-ContextPath <path>] [-OutFile <path>]
script: ./capture-style.ps1
shell: powershell
---

# Capture Style

Execute `/capture-style` to assemble the style-capture prompt from required writing samples and optionally hydrate it with a context file.

## Intent

Turn required writing samples into a reusable STYLE_CARD that captures authorial invariants without copying distinctive expression.

## Arguments

- `<sample-file...>`: One or more local writing-sample files when the user has not attached or uploaded samples with the command.
- `-ContextPath <path>`: Optional file describing domain, audience, purpose, and taboo constraints.
- `-OutFile <path>`: Optional path to save the assembled prompt for reuse.

## Execution

1. Writing samples are required. First inspect the user's attached or uploaded sample files and any pasted sample text in the same message.
2. If attachments or uploads are present, parse and extract their text, preserve source boundaries, and inject the material directly into `<SAMPLES>` before running the prompt.
3. If no attachments, uploads, or pasted samples are present, require one or more local sample file paths and run the paired script to combine them into a ready-to-paste prompt.
4. If a context file is provided, inject it into `<CONTEXT>`; otherwise leave the optional context placeholder.
5. Keep samples in the same mode you want to emulate, such as essays, memos, or emails.
6. Ensure the response includes `STYLE_DNA`, `STYLE_CARD`, and `CONFIDENCE NOTES`.
7. Reuse the resulting STYLE_CARD with `/mimic-style`.

### Prompt Template

```text
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
[Inject parsed writing samples here.
If there are multiple samples, keep each sample labeled by source name and separated clearly.
Keep samples in the same mode you want to emulate, such as essays vs emails.]
</SAMPLES>

<CONTEXT>
(Optional) Intended output domain, audience, purpose, and taboo constraints.
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
```

## Output

- A hydrated prompt from the helper script or the template above.
- Sample materials injected from attachments, uploads, pasted text, or local files, with source boundaries preserved.
- A model result containing `STYLE_DNA`, `STYLE_CARD`, and `CONFIDENCE NOTES`.
- Explicit guardrails against copying distinctive phrases or anecdotes.
