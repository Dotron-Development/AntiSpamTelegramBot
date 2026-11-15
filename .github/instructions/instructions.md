# Repository instructions

## C# imports

- Keep all explicit C# import directives in each project's existing global-usings file (`GlobalUsings.cs`, `GlobalUsing.cs`, `Globalusings.cs`, or test `Usings.cs`).
- Use `global using` directives; do not add file-local or namespace-local imports to other source files. Add new namespaces to the corresponding project's global-usings file without duplicating existing imports.
- Keep imports scoped to their project. Preserve SDK-generated implicit usings and existing global-usings filenames.
- Resolve ambiguous type names with explicit `global using` aliases in the same file, preserving the types used before moving imports.
- This rule concerns import directives only; resource-management statements such as `using var`, `using (...)` and `await using` stay where resources are used.
