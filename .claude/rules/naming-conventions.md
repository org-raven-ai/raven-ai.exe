# Naming conventions

## Files

- File names use **kebab-case**: `api-client.ts`, `chat-sidebar.tsx`, `use-provider.ts`.

## Identifiers

Follow standard TypeScript casing, with one rule for abbreviations.

- **camelCase** — variables, functions, methods, object properties.
- **PascalCase** — classes, types, interfaces, enums, React components, and other constructs that are conventionally capitalized.

### Abbreviations

Treat an abbreviation as a single unit whose letters all share **one** case, determined by the abbreviation's position in the identifier:

- If that position is lowercase (e.g. the start of a camelCase name), the whole abbreviation is **lowercase**.
- If that position is uppercase (e.g. a capitalized word-start, or the start of a PascalCase name), the whole abbreviation is **uppercase**.

Never write `Api`, `Url`, or `Id` — the mixed case is wrong.

**camelCase examples:**

| Correct    | Wrong       |
| ---------- | ----------- |
| `api`      | —           |
| `apiURL`   | `apiUrl`    |
| `userID`   | `userId`    |
| `parseHTML`| `parseHtml` |

**PascalCase examples (classes, types, components):**

| Correct        | Wrong           |
| -------------- | --------------- |
| `APIClient`    | `ApiClient`     |
| `HTTPRequest`  | `HttpRequest`   |
| `UserID`       | `UserId`        |