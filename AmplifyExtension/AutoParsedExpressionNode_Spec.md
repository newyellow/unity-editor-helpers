# Auto Parsed Expression Node Specification

> Target File: `Assets/_Nyamyam/ExtensionTools/Editor/AutoParsedExpressionNode.cs`

This document explains the behavior, input format, and limitations of this custom Amplify Shader Editor node for reference by others or AI.

## 1. Comment Format (Port Generation Rules)

The script scans each line of code and detects the following comment tags:

- Input: `//@in <type> <Name>`
- Output: `//@out <type> <Name>`

### Naming Rules

- `<Name>` must be a valid identifier: `^[A-Za-z_][A-Za-z0-9_]*$`
- Lines that do not follow this rule will be ignored.

### Supported Types

The following types are currently supported (case-sensitive):

- float / float2 / float3 / float4
- half / half2 / half3 / half4 (equivalent to the float series)
- int
- sampler2D / sampler3D / samplerCUBE
- color
- bool (mapped to INT in practice)

> Note: Shaders do not have a true boolean execution type, so `bool` corresponds to an `INT` port.

## 2. Output Port Rules

- Only a single output is allowed.
- If multiple `//@out` tags are present, only the first one will be used.
- If no `//@out` tag is found, it defaults to: `float Out`.

## 3. Input Port Rules

- All valid `//@in` tags will generate ports in order.
- The order of ports matches the order in which the comments appear.

## 4. Connection and Port Deletion Behavior

When rebuilding ports, existing ports are completely removed first:

- All input/output connections are severed, and ports are removed.
- If any remain, they are deleted one by one by index.

This process uses the public API of `ParentNode` to avoid using reflection.

## 5. Shader Code Rules

### main() and Helper Functions

- A node can contain multiple helper functions and one `main(...)`.
- All functions are automatically prefixed with a unique ID to avoid name collisions.

### Case with No main()

- If no `main(...)` function is found, the entire code block is treated as a "single-line expression" and output directly.

## 6. bool Mapping and 0/1

- `bool` ports are actually `INT`.
- Values are not automatically converted to 0 or 1; the value depends on the connected input source.
- If a 0/1 value is required, you must convert it yourself upstream or within the code.

## 7. Behavior Summary (Quick Checklist)

- Only processes `//@in` and `//@out` comments.
- `bool` is treated as `INT`.
- Only the first output is kept.
- Rebuilding clears old ports and connections first.
- The script renames all functions to avoid conflicts.

---

If the specification changes, please update this document accordingly.
