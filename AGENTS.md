# Repository Guidelines

## Project Structure & Module Organization

`SurveyBackend.slnx` contains one ASP.NET Core 10 project, `src/SurveyBackend/`. HTTP endpoints belong in `Controllers/`; entities and enums in `Models/`; EF Core context and migrations in `Data/`. `Bot/Commands/` contains OneBot handlers, with shared interfaces and base classes in `Infrastructure/`. Use `BackgroundServices/` for scheduled work, `Services/Insights/` and `Services/Statistics/` for domain services, and `Configuration/` for typed options. `Program.cs` wires services and middleware. Root files include `version.txt`, `Dockerfile`, and `readme.md`; `.github/workflows/` publishes main/dev builds. No frontend assets or test projects are currently included.

## Build, Test, and Development Commands

Run from the repository root with the .NET 10 SDK:

- `dotnet restore SurveyBackend.slnx` — restore NuGet dependencies.
- `dotnet build SurveyBackend.slnx -c Release` — compile the solution.
- `dotnet run --project src/SurveyBackend/SurveyBackend.csproj --launch-profile https` — start locally at `https://localhost:7224` (HTTP: `5136`).
- `dotnet publish src/SurveyBackend/SurveyBackend.csproj -c Release -r linux-x64 --self-contained false -o publish/linux-x64` — produce a runtime-dependent build, matching CI; use `win-x64` for Windows.

Configure MySQL and OneBot before running. Development exposes `/openapi/v1.json`.

## Coding Style & Naming Conventions

Use four-space indentation and one top-level type per file. Match filenames to types and directory-based `SurveyBackend.*` namespaces. Follow `.editorconfig`: UTF-8, file-scoped namespaces, imports outside namespaces, and System imports first. Use PascalCase for types/methods/properties, camelCase for parameters/locals, and `I` prefixes for interfaces. Follow neighboring private-field conventions. Centralize common imports in `GlobalUsings.cs`. Nullable references are enabled; preserve null handling. No separate lint or formatting gate is configured.

## Testing Guidelines

No test framework or coverage threshold is configured; CI currently publishes builds only. Build before submitting, then validate affected API routes, `/survey` commands, permissions, and review transitions with disposable data and test groups. Record steps and results. For new automated coverage, use a `SurveyBackend.Tests` project, descriptive names such as `Method_Condition_ExpectedResult`, and `dotnet test SurveyBackend.slnx` after adding it to the solution.

## Commit & Pull Request Guidelines

Do not create Git commits unless explicitly requested by the user. When authorized, keep commit messages and code comments concise and natural; avoid verbose, formulaic AI-style commentary.

History uses `feat:`, `fix:`, `refactor:`, `build:`, and `version:` prefixes, often with Chinese descriptions. Keep commits focused; update `version.txt` for releases. PRs should explain behavior changes, link relevant issues, report validation, and document configuration or migration requirements.

## Security & Configuration Tips

Copy `src/SurveyBackend/appsettings.example.json` to the same directory as `appsettings.json`; keep credentials out of Git. Supply the configured prompt file for AI insights. Restart after configuration changes. Database migrations are not automatic: review generated SQL and back up data before applying schema changes.
