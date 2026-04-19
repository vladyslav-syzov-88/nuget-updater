# Prompts

This directory contains prompt rules and templates for AI-assisted development on the Account Management Service.

## Structure

```
.prompts/
├── README.md          # This file
└── rules/             # Prompt rules for code generation and review
```

## Rules

The `rules/` directory contains rule files that guide AI agents when working with this codebase. Each rule file focuses on a specific aspect of development (e.g., coding standards, testing patterns, architecture).

### Available Rules

- **csharp-conventions.md** - C# coding standards and patterns used in this project
- **testing.md** - Testing guidelines and patterns (NUnit, Moq)
- **architecture.md** - Architectural decisions and domain organization rules
- **coding-style.md** - Formatting and style rules derived from `.editorconfig`
