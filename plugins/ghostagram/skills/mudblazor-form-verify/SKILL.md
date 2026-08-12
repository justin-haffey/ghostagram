---
name: mudblazor-form-verify
description: Verifies Ghostagram MudBlazor forms through exact package resolution, Release build and tests, and mandatory visible-browser behavior and console inspection. Use after creating or changing a MudForm, field validation, submit flow, or responsive form layout. Do not use as a substitute for browser testing or to install or upgrade packages.
---

# Verify a MudBlazor Form

Prove that a compact Ghostagram form resolves the intended package, compiles, passes relevant tests, and behaves correctly in a visible browser.

## Core Contract

All four gates are mandatory: package resolution, build, tests, and visible-browser behavior with console inspection. Report each gate separately. Headless DOM checks, HTTP health, screenshots without interaction, or a successful build do not replace the visible-browser gate.

Do not edit package references, install templates or form libraries, or repair product code unless the user separately authorizes a fix. Treat this skill as read-only verification except for normal build outputs and temporary server ownership state.

## Workflow

1. Read repository instructions, the form's field/validation/submit contract, and the current worktree. Identify the reusable `Ghostagram.Blazor` form and its `Ghostagram.Server` host without disturbing unrelated changes.
2. Inspect both project files. Require one direct `MudBlazor` reference resolving to `9.8.0` in each project, require Server to reference Blazor, and reject `MudBlazor.Templates` or third-party dynamic-form packages.
3. From the repository root, restore only when required to produce current assets:

   ```powershell
   dotnet restore .\Ghostagram.slnx
   ```

   Do not change dependency declarations or lockfiles. If network, credentials, or package feeds block restore, stop the package gate and report the exact failure.
4. Record requested and resolved packages without another restore:

   ```powershell
   dotnet list .\src\Ghostagram.Blazor\Ghostagram.Blazor.csproj package --include-transitive --no-restore
   dotnet list .\src\Ghostagram.Server\Ghostagram.Server.csproj package --include-transitive --no-restore
   ```

   Require direct `MudBlazor` requested/resolved version `9.8.0` for both projects. Do not treat the presence of a package-reference line alone as resolution proof.
5. Build the complete solution against those assets:

   ```powershell
   dotnet build .\Ghostagram.slnx -c Release --no-restore
   ```

6. Run every executable .NET verification project without rebuilding, then run the JavaScript suite from `src/Ghostagram`:

   ```powershell
   dotnet run --project .\tests\Ghostagram.Core.Tests\Ghostagram.Core.Tests.csproj -c Release --no-build
   dotnet run --project .\tests\Ghostagram.Execution.Tests\Ghostagram.Execution.Tests.csproj -c Release --no-build
   dotnet run --project .\tests\Ghostagram.Persistence.Tests\Ghostagram.Persistence.Tests.csproj -c Release --no-build
   dotnet run --project .\tests\Ghostagram.Layout.Verification\Ghostagram.Layout.Verification.csproj -c Release --no-build
   cmd.exe /d /c npm.cmd test
   ```

   These repositories use console executable verifiers, so `dotnet test` alone does not execute their checks. Use `src/Ghostagram` as the working directory for the npm command. Also run any feature-specific test project or repository validator named by current instructions. Record pass/fail/skip counts; do not collapse skipped or undiscovered tests into a pass.
7. Use `$start-server` to start or reuse the loopback development host. Retain its URL and ownership status.
8. Use the available in-app Browser control skill to open the real Server route containing the form. A browser must be visible and interactive. If no browser is available, mark browser verification blocked and do not claim the form verified.
9. Inspect the browser console before interaction. Verify the expected labels, controls, option values, compact layout, required indicators, initial values, and enabled/disabled state against the contract at the normal viewport and at one narrow viewport.
10. Exercise one invalid path per validation category. Attempt submit and require field/cross-field messages, retained user input, focus or clear association to the invalid field, and no server callback or navigation.
11. Exercise a safe valid path. Require typed values to reach the Server handler once, busy state to prevent duplicate submit, and the contracted success/error feedback. Do not trigger a real irreversible write unless the user explicitly authorized it; use the feature's test/local handler or stop and request a safe verification target.
12. Verify keyboard traversal and Enter/submit behavior. Exercise cancel/reset only when present in the contract.
13. Reinspect the browser console after every scenario. Treat unhandled exceptions, Blazor renderer errors, failed resource loads, and MudBlazor JavaScript errors as failures; separate known unrelated warnings with evidence.
14. Capture visible evidence for initial, invalid, and valid states. Stop a server only when this run owns it and it is no longer needed; use `$stop-server` rather than killing by port or process name.

## Failure Rules

- Do not retry a deterministic build, validation, or browser failure unchanged.
- Retry one transient restore, launch, or browser-navigation failure only after identifying it as transient.
- Do not weaken the field contract, suppress console errors, or change persistence behavior to make verification pass.
- Never report browser success from source inspection, HTTP status, MCP state, or a headless substitute.

## Output

Return a gate table for package resolution, build, tests, visible behavior, and console state. Include exact commands, exit codes, test counts, host URL/ownership, browser scenarios and evidence, console errors, changed files (normally none), and blockers. The overall result is PASS only when every mandatory gate passes.
