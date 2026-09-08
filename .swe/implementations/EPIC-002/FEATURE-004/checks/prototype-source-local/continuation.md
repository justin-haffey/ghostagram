# Ghostagram prototype source checkpoint

This supersedes source-ready/continuation.md for current work, while preserving that historical checkpoint.

- Active prototype run: PROTOTYPE-RUN-20260907T094049Z. Parent owns prototype state/run and closure. Scope Ghostagram F004 and F003 only.
- System holds the shared build slot. No source-phase build, test or restore has run. Await explicit parent release and actual System candidate fingerprint.
- F004 source now uses separate GraphLocal snapshots, batches and runtime integration. Host codec is local schema 1 with complete-history capture and explicit history-unavailable-only fallback. No custom vocabulary qualification or negotiated profile is introduced.
- Compiler exposes local and governed input overloads; governed input is explicitly downgraded to local inspection. Diagram compilation retains immutable GraphCompilationInput and General finite capacity (100,000 nodes/250,000 relationships). Source tests cover exact/+1/invalid limits and default diagrams above 256 nodes/1024 edges.
- Target3 remains Pending; Accepted Target2 archives and Accepted Design1 unchanged. Prototype defers upfront gates; reconstruct truthful as-built Draft/Target after execution evidence and obtain independent review. No self-approval.
- F003 foundation implementation is underway in DeclarativeCompositionProjection and matching tests. Actual System PublicView and consumer envelope admission remain dependencies. No fabricated Fresh output or private aggregate filtering.
- Preserve baseline-original, baseline-repaired-release and source-ready evidence exactly; checkpoint hashes are provisional, not final tested candidate hashes.
- Build uses dotnet build Ghostagram.slnx -c Release -p:ShouldUnsetParentConfigurationAndPlatform=false once authorized. Then run 7 ordinary suites and 6 existing conformance commands, capturing new logs and envelopes separately from accepted baseline history.
