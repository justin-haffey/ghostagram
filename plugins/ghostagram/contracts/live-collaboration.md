# Live human and agent collaboration

Use this workflow when a human and one or more agents are editing the same Ghostagram document.

1. Start or health-check the canonical `http://127.0.0.1:5256` host.
2. Call `describe_capabilities`; use its schema instead of guessing model or operation fields.
3. Obtain the document ID from the human, or call `list_diagrams` and choose an explicitly authorized live document.
4. Give every participant a distinct, stable actor ID. Open the known document; create only with explicit creation intent.
5. Full-sync before the first write. Prepare each coherent batch against the returned current revision and use a new command ID.
6. Verify the command and semantic postconditions with `get_diagram` from the original base revision.
7. When human-visible rendering matters, call `list_diagrams`. A positive view count plus `oldestBrowserRevision >= committedRevision` proves every current integrated Laboratory view acknowledged that revision. Otherwise report the authoritative commit and browser state separately.
8. Use the direct `browserUrl` for visual review. Inspect the expected elements and browser console after renderer-facing changes.
9. Resolve revision conflicts once only when intent remains additive and unambiguous. Stop on destructive or ambiguous merges.
10. Close only the caller's participant after all writes and review are reconciled.

The local server has no authenticated internet-facing boundary. Do not expose it beyond loopback as a collaboration shortcut.
