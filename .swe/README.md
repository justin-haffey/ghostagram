# Solution Engineering Work

Implementation work is keyed by the upstream Epic and Feature IDs:

```text
implementations/EPIC-001/FEATURE-001/
  DESIGN.md
  EVIDENCE.md
  VALIDATION.md
changes/
  bugs/BUG-001-short-name/BUGFIX.md
  enhancements/ENH-001-short-name/ENHANCEMENT.md
```

Every implementation workspace links to the canonical portfolio `FEATURE.md` and adjacent `IMPLEMENTATION-PLAN.md` using repository, stable artifact ID, repository-relative path, and optional revision. Do not copy either upstream artifact locally. Fast paths remain local and bounded; escalate them when accepted architecture, a cross-solution contract, Feature intent, or portfolio allocation changes.
