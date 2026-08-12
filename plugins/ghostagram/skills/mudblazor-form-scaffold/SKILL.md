---
name: mudblazor-form-scaffold
description: Scaffolds compact, strongly typed MudBlazor forms across Ghostagram.Blazor reusable UI and Ghostagram.Server feature ownership. Use when adding or restructuring a Ghostagram MudForm, typed field model, validation, or submit callback. Do not use for persistence design, package installation, generated MudBlazor templates, or third-party dynamic forms.
---

# Scaffold a MudBlazor Form

Create the smallest compiling form shell whose fields, validation, and submit behavior are explicit before code is written.

## Core Contract

Keep reusable presentation in `src/Ghostagram.Blazor` and feature orchestration in `src/Ghostagram.Server`. Use the official `MudBlazor` package already referenced directly at version `9.8.0` by both projects. Do not add or change package references, host registration, `MudBlazor.Templates`, reflection-driven form generation, or third-party dynamic-form packages.

Capture this contract before editing:

- feature name and host page or component;
- every field's property name, .NET type, label, control, default, required state, constraints, options source, and editability;
- field-level and cross-field validation rules, including exact messages when supplied;
- valid-submit payload and callback, cancel/reset behavior, busy/disabled behavior, and success/error presentation;
- the user-authorized server action, if any.

If a required contract choice is missing, ask up to three focused questions. Do not infer storage, HTTP calls, database writes, diagram mutations, or other persistence.

## Ownership Boundary

| Owner | Responsibilities |
| --- | --- |
| `Ghostagram.Blazor` | Feature-specific reusable form component; strongly typed mutable presentation model or other existing typed form contract; compact MudBlazor controls; validation display; loading/disabled state; typed valid-submit and optional cancel callbacks. |
| `Ghostagram.Server` | Feature host/page; initial values and option loading; mapping between presentation and application types; authorization; calling an existing user-approved service; success/error state and navigation. |

Do not inject repositories, HTTP clients, persistence services, or server-only types into the Blazor form. Put a type in `Ghostagram.Contracts` only when it is already a genuine cross-process contract; a form model alone is not sufficient reason.

## Workflow

1. Read repository instructions and inspect the current worktree. Preserve unrelated and concurrent changes.
2. Verify both project files still resolve direct `MudBlazor` `9.8.0` references and that the Server project references Blazor. Stop before editing if this boundary has changed.
3. Inspect only the target host and the nearest reusable Blazor components to follow current namespaces, partial-class style, CSS isolation, and test conventions.
4. Write the field/validation/submit contract in the task notes. Map every requested field to a strongly typed property; never use `object`, string-keyed dictionaries, runtime reflection, or guessed enum values as a shortcut.
5. Add the reusable form under the nearest established `Ghostagram.Blazor` component area. Use `MudForm` with compact `MudGrid`/`MudItem` layout and the appropriate typed MudBlazor input. Prefer existing theme spacing and component parameters over new global CSS.
6. Expose an explicit typed valid-submit callback. Validate before invoking it, invoke it once per accepted submit, disable repeat submission while busy, and keep invalid submissions inside the reusable component. Add cancel/reset only when the contract includes it.
7. Add or adapt the `Ghostagram.Server` host in the feature's existing area. It owns model initialization, option sources, service calls, feedback, and navigation. When no server action is authorized, wire only the typed callback boundary and state that persistence remains intentionally unimplemented.
8. Preserve field values and surface actionable errors after a failed server action. Do not log secrets or validation-sensitive values.
9. Add focused tests using only the repository's existing test infrastructure. If there is no compatible component-test harness, do not add one implicitly; hand the result to `$mudblazor-form-verify` for mandatory browser verification.
10. Review the diff for scope, accessibility, complete labels/helper text, keyboard submission, and accidental package or persistence changes.

## Form Rules

- Use a real typed model and typed control values, including nullable types where an empty value is valid.
- Keep labels visible, connect validation messages to their fields, and preserve logical keyboard order.
- Treat client validation as user feedback, not authorization or a server trust boundary.
- Use server-provided option collections rather than embedding domain data in the reusable component.
- Keep layout compact without shrinking hit targets, hiding required indicators, or relying on placeholder text as the label.
- Do not claim a MudBlazor API is valid from memory alone; confirm against the resolved version through compilation.

## Validation

Run `$mudblazor-form-verify` after scaffolding. At minimum, require exact package-resolution evidence, a Release build, relevant tests, visible browser interaction, and a clean browser console. A compile-only result is incomplete.

## Output

Return the Blazor and Server paths, the field/validation/submit contract, the ownership split, tests and browser checks run, and any deliberately unimplemented server action. Call out every missing contract choice or blocked validation gate.
