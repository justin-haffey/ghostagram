---
description: Generate new text from a STYLE_CARD with content and anti-copy guardrails
argument-hint: <style-card-file> <spec-file> [draft-file] [-OutFile <path>]
script: ./mimic-style.ps1
shell: powershell
---

# Mimic Style

Execute `/mimic-style` to assemble the style-transfer prompt from an existing STYLE_CARD and a new document specification.

## Intent

Generate original writing that preserves required meaning while following the target style's structural tendencies, not its memorable wording.

## Arguments

- `<style-card-file>`: File containing the STYLE_CARD produced by `/capture-style`.
- `<spec-file>`: File containing the new document specification or rewrite brief.
- `[draft-file]`: Optional draft to evaluate with the style critic prompt.
- `-OutFile <path>`: Optional path to save the assembled prompt bundle.

## Execution

1. Run the paired script with a STYLE_CARD and NEW_DOC_SPEC to build the generation prompt.
2. If you also provide a draft file, the helper script emits the optional style critic prompt for evaluation and revision.
3. Use Candidate A for strongest fidelity and Candidate B for a lighter-touch variant.
4. Review factual correctness and constraint compliance before optimizing for style.

### Generation Prompt

```text
SYSTEM:
You are an expert writer. You will generate new, original text that follows a provided STYLE_CARD.
You must not copy phrases from the samples. You must preserve meaning requirements.
If there is any tension between requirements, prefer: (1) factual correctness, (2) safety,
(3) the user's constraints, then (4) style fidelity.

USER:
TASK: Write a new document in the style defined by STYLE_CARD.

INPUTS:
<STYLE_CARD>
[Paste the STYLE_CARD produced by the style capture prompt.]
</STYLE_CARD>

<NEW_DOC_SPEC>
- Document type:
- Audience:
- Purpose:
- Length target (words or sections):
- Required points / outline:
- Constraints (must include / must avoid):
- Source material (if rewriting): paste text here; otherwise leave blank.
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
```

### Optional Style Critic Prompt

```text
SYSTEM:
You are a style-transfer evaluator. You grade and revise text against:
(1) style match to STYLE_CARD, (2) content preservation vs spec or source, and (3) fluency.

USER:
INPUTS:
<STYLE_CARD>...</STYLE_CARD>
<NEW_DOC_SPEC>...</NEW_DOC_SPEC>
<DRAFT>...</DRAFT>

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
```

## Output

- A hydrated generation prompt and, when requested, a critic prompt.
- Candidate A and Candidate B from the model run, plus Style Compliance Notes.
- Optional scored revision loop for style match, content preservation, fluency, and anti-copy review.
