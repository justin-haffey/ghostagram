# Serialization compatibility

## Compatibility rule

Existing diagram JSON remains valid. New fields are optional and appear at the end of C# record constructors. Missing node properties normalize to an empty collection; ports without a property association use their existing anchor behavior.

Loading an older file does not automatically commit or rewrite it. The file changes only after a real user or API command.

## Explicit null

An explicit property value of JSON `null` is data and must survive replace, apply, inspect, server commit, reload, and export. Null cleanup may remove optional descriptor fields, but it must not recursively remove property values.

`dateTime` values use ISO 8601. The browser presents them through a local date/time control and commits a normalized UTC ISO value; `date` values remain calendar-only strings.

## Forward compatibility

Typed deserialization and re-emission must preserve supported property records and registered custom data-type identifiers. Provider-native execution objects, CLR type names, DI service types, and concurrency primitives are never serialized.

## Versioning

`TypeId` and `TypeVersion` identify the reusable node definition that created an instance. The instance still carries enough property and port metadata to render without the definition assembly. A registry may offer an explicit upgrade command later; opening a document does not silently migrate it.

Execution checkpoints also carry the compiled-plan fingerprint. A checkpoint created for one graph revision cannot resume a materially different plan.
