---
description: Coding style rules enforced by .editorconfig — follow these when writing or modifying code
globs: **/*.{cs,csproj,json,resx}
---

# Coding Style

All formatting and style rules are defined in the repository root `.editorconfig` file. Always follow these rules when writing or modifying code.

## Indentation and Whitespace

- Use **tabs** for indentation (size 4) in all files except Dockerfiles (which use spaces)
- Line endings: **CRLF**
- Charset: **UTF-8**
- Trim trailing whitespace
- Do **not** insert a final newline

## C# Style

### Namespaces and Usings

- Use **file-scoped namespaces** (`namespace Foo;`)
- Sort `System` directives first
- Do not separate import directive groups with blank lines

### Type Keywords

- Use language keywords for built-in types (`int`, `string`) instead of framework types (`Int32`, `String`)
- Do not qualify members with `this.`

### `var` Usage

- Use `var` only when it is required by language and specific type cannot be used, like with anonymous types
- Do **not** use `var` for built-in types — spell them out (`int count = 0`, not `var count = 0`)

### Expression-Bodied Members

- Properties, indexers, accessors, lambdas: always use expression bodies
- Methods, operators, local functions: use expression bodies only when single-line
- Constructors: do **not** use expression bodies

### Pattern Matching

- Prefer pattern matching over `is` with cast checks and `as` with null checks
- Prefer switch expressions and `not` patterns

### Null Handling

- Use null propagation (`?.`), null coalescing (`??`), and throw expressions
- Use `is null` instead of `== null`

## Braces and Formatting

- Opening braces on a **new line** (Allman style) for all constructs
- `else`, `catch`, `finally` on a new line
- Do not put multiple statements on a single line
- Single-line blocks are allowed

## Naming Conventions

| Symbol                      | Style              | Example                   |
|-----------------------------|--------------------|---------------------------|
| Types (class, struct, enum) | PascalCase         | `BillingAccount`          |
| Interfaces                  | `I` + PascalCase   | `IBillingAccountManager`  |
| Methods, properties, events | PascalCase         | `GetBillingAccountAsync`  |
| Async methods               | PascalCase + Async | `CreateAccountAsync`      |
| Constants                   | PascalCase         | `MaxRetryCount`           |
| Private instance fields     | `_camelCase`       | `_billingAccountManager`  |
| Private static fields       | PascalCase         | `DefaultTimeout`          |
| Parameters and locals       | camelCase          | `customerId`              |

### Modifiers

- Always specify accessibility modifiers explicitly
- Preferred modifier order: `public`, `private`, `protected`, `internal`, `static`, `extern`, `new`, `virtual`, `abstract`, `sealed`, `override`, `readonly`, `unsafe`, `volatile`, `async`

## ReSharper Settings

- Wrap list patterns: chop always
- Max initializer elements on line: 1
