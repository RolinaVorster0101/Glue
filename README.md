# Glue

*Everything your code needs, wired together.*

Glue is a personal, daily-driver IDE built from the ground up — one unified tool instead of a dozen separate apps stitched together. It's designed to genuinely outperform the alt-tab-between-tools workflow: editor, multi-language intelligence, debugging, database management, Git/GitHub, and secrets, all in one flow, with security and consistent code style baked in by default rather than bolted on.

## Why Glue exists

Most modern tools have quietly become black boxes. You don't always know what's being sent over the network, what a background service is phoning home with, or when the next forced update is going to change how the tool behaves — or make you click "accept" on a new policy just to keep working. Glue is built the opposite way: you know exactly what leaves your machine and when, because you built the pipeline yourself. Nothing calls out to the internet unless a specific feature needs it (an LLM advisory call, a GitHub push), and even then, the boundary of what's sent is explicit and inspectable — not something buried in a vendor's telemetry you have to trust blindly.

Security is the same story. Most tools treat it as something you bolt on afterward — a linter you remember to run, a checklist you follow if you have time. Glue treats it as a structural default: secure scaffolding, parameterized queries, and OS-vault-only secrets aren't optional extras, they're just how the tool works. The result is cleaner, safer code by default, not code that's safe because you remembered to make it so.

And beyond that — a lot of what a modern IDE actually needs is scattered across separate apps that were never meant to talk to each other: your editor, your debugger, your database client, your Git tool. Secrets are a slightly different case — the actual encrypted storage is OS-level infrastructure (Windows Credential Manager), and Glue doesn't reinvent that. What it removes is the separate app you'd otherwise open to manage it: Glue talks to that same vault directly from an in-IDE panel, so adding or viewing a secret never means leaving your code to go find it somewhere else. None of that separation is necessary. It's not that those tools are missing features — it's that the *IDE itself* is missing the basics that should've been structural from day one: knowing your schema, your repo, your secrets, and your conventions, and acting on that knowledge without you gluing it together by hand every time. That's the whole point of the name.

## Core principles

- **Own the whole pipeline.** Built on open, forkable, non-proprietary foundations — nothing here depends on a vendor's servers at runtime.
- **No hidden network activity, no forced updates.** Nothing calls out to the internet unless a specific feature needs it, and the boundary of what gets sent — and to whom — is always explicit. No silent background telemetry, no update popups demanding you accept a new policy just to keep working.
- **Multi-language from the start.** C# via Roslyn, everything else via the Language Server Protocol (Go, C/C++, TypeScript/JavaScript, Python) and the Debug Adapter Protocol for debugging — one consistent experience across languages, not a C#-only tool with plugins bolted on.
- **Security by default, not by memory.** Scaffolding never includes outdated/insecure defaults (no Bootstrap, no jQuery), secure code patterns (file upload validation, parameterized queries, CSRF tokens) are pre-wired into templates, and secrets are stored in the OS-level encrypted vault — never in a plaintext file a `git add .` could ever pick up.
- **Explain, don't just do.** An advisory analysis layer surfaces issues an LLM catches beyond static analysis — with a real fix and example code to review, not a silent rewrite. A separate, optional, clearly-bounded agent layer exists for actual autonomous task execution, but the two are never blurred.
- **Everything in one flow.** Database management, Git/GitHub operations, and secrets management live inside the IDE as integrated panels — not separate apps you switch between.

## What's inside

| Area | Highlights |
|---|---|
| **Editor core** | AvaloniaEdit-based, fully customizable, code folding (persisted per file), Format Document driven by your own `.editorconfig` |
| **Language intelligence** | Roslyn for C# — ✅ live diagnostics, completion, Go to Definition, Find All References, Rename, Extract Method, all project-aware. LSP for Go/C++/TS/Python/CSS — planned, not yet built (Phase 5) |
| **CSS Quick Actions** | Right-click a selector to scaffold a media query (with personal default breakpoints), a `:hover`/`:focus`/`:active` block, or a dark-mode override — powered by the CSS language server, no manual boilerplate typing (Phase 5) |
| **Scaffolding & templates** | A "New Project" wizard with a custom template engine — house-style boilerplate across C#/.NET, Node.js, React, TypeScript, and Rust, with a correct `.gitignore` included automatically every time |
| **Security snippets** | Pre-vetted secure code blocks (file upload validation, parameterized queries, CSRF tokens) auto-inserted for known-risky scaffolding scenarios |
| **Debugging** | DAP-based: `netcoredbg`, `delve`, `lldb-dap`/cpptools — conditional breakpoints, watch/immediate windows, Hot Reload |
| **Database Workbench** | Integrated, multi-provider (MySQL, Postgres, SQL Server, SQLite) via a common `IDbProvider` abstraction — schema browser, query editor, editable results grid, ER diagrams, EXPLAIN visualization |
| **Git & GitHub** | LibGit2Sharp for local operations, Octokit.net for GitHub (repo creation, push/pull/sync), OAuth device flow auth, plus a GitHub contributions activity widget |
| **Secrets Manager** | Project-scoped, stored in the OS-level encrypted credential vault, structurally isolated from the LLM advisory layer — the model has no code path to reach secret values, ever |
| **Advisory Analysis Layer** | Tier 1: deterministic Roslyn/LSP diagnostics. Tier 2: LLM-powered advisory findings with reviewable example fixes — never auto-applied for anything sensitive |
| **Autonomous Agent Layer** *(optional, last phase)* | Task-level agent execution with plan → write → build/test → report, inspired by tools like Google Antigravity — kept explicitly separate from the advisory layer |
| **Integrated terminal** | ✅ Built — basic persistent PowerShell shell, plain text I/O. Not a full terminal emulator (no ANSI colors, no full-screen apps) |
| **Command Palette** | ✅ Built (`Ctrl+Shift+P`) — substring-searchable list of every command, 28 registered so far, grows as new features land |
| **Settings & Preferences** | ✅ Built (`Ctrl+,`), scoped to what's actually functional today: editor font/size/indentation, Format on Save, persisted to disk. Theme/keybindings/LSP paths/templates/Advisory Layer settings will grow in as those features get built |

## Tech stack

| Layer | Technology |
|---|---|
| Shell / UI | **Avalonia** (.NET 8, cross-platform WPF-alike) |
| Editor core | **AvaloniaEdit** (Avalonia's port of AvalonEdit) |
| C# intelligence | **Roslyn** (`Microsoft.CodeAnalysis.CSharp`), hosted in-process |
| Other languages | **Language Server Protocol (LSP)** — `gopls` (Go), `clangd` (C/C++), `typescript-language-server` (TS/JS), `pyright`/`pylsp` (Python), a `vscode-css-languageservice`-based server (CSS/SCSS/LESS) |
| Debugging | **Debug Adapter Protocol (DAP)** — `netcoredbg` (C#), `delve` (Go), `lldb-dap`/cpptools (C/C++), `debugpy` (Python) |
| Database | `MySqlConnector`, `Npgsql`, `Microsoft.Data.SqlClient`, `Microsoft.Data.Sqlite` behind a common `IDbProvider` abstraction |
| Git & GitHub | **LibGit2Sharp** (local git), **Octokit.net** (GitHub REST + GraphQL) |
| Secrets | Windows Credential Manager (DPAPI-encrypted OS vault) via `CredentialManagement` |
| Advisory layer | Anthropic API (Claude), called only with explicit, redacted context — never given access to secrets |

All of the above are open source / MIT-or-equivalent licensed and run locally — nothing here depends on a vendor's servers at runtime except the explicit, opt-in Advisory Layer call.

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Windows (Credential Manager integration and the current DPI/manifest setup target Windows first; cross-platform support is a later consideration)
- As multi-language support comes online (Phase 5), the relevant language server/debugger binaries per language: `gopls`, `clangd`, `typescript-language-server`, `pyright`, `netcoredbg`, `delve`, etc.

## Getting Started

```bash
git clone <this-repo>
cd Glue
dotnet restore
dotnet run
```

This launches the current shell: a real editor with C#-aware syntax highlighting, live Roslyn diagnostics and completion, code folding, Format Document, an Explorer sidebar, and — once you load a project via File > Open Project — true cross-file navigation (Go to Definition, Find All References, Rename) plus real `dotnet build`/`dotnet run` integration.

## Project structure

```
Glue/
  Glue.csproj          # Project file — Avalonia, AvaloniaEdit, Roslyn, MSBuildWorkspace references
  Program.cs           # Avalonia entry point (MSBuildLocator.RegisterDefaults() runs first)
  App.axaml(.cs)        # Application-level setup, theme, DevTools (Debug-only)
  MainWindow.axaml(.cs) # Main shell window: menu, status bar, editor, Explorer, Problems/Output/References/Terminal tabs
  app.manifest          # Windows DPI/theming manifest
  Models/
    FileTreeNode.cs      # Explorer sidebar's file tree node model (with modified-file indicator support)
  Services/
    RoslynDiagnosticsService.cs    # Live diagnostics (single-file + project-aware)
    RoslynCompletionService.cs     # Basic completion (single-file + project-aware)
    RoslynFoldingService.cs        # Code folding via Roslyn syntax tree
    RoslynFormattingService.cs     # Format Document via Roslyn's formatter
    RoslynNavigationService.cs     # Go to Definition, Find All References, Rename
    RoslynExtractMethodService.cs  # Extract Method via SemanticModel.AnalyzeDataFlow
    RoslynProjectService.cs        # MSBuildWorkspace — true project parsing
    RoslynReferences.cs            # Shared reference-assembly helper
    FoldStateStore.cs              # Per-file fold state persistence
    ProjectTreeBuilder.cs          # Explorer sidebar's folder→tree builder
    BuildService.cs                # Real `dotnet build` subprocess
    RunService.cs                  # Real `dotnet run` subprocess
    TerminalService.cs             # Basic interactive shell (persistent PowerShell subprocess)
    SettingsService.cs             # Persisted editor/formatting preferences
  Views/
    RenameDialog.axaml(.cs)          # "Enter new name" dialog
    ConfirmRenameDialog.axaml(.cs)   # Lists affected files before Rename writes anything
    UnsavedChangesDialog.axaml(.cs)  # Save All / Discard / Cancel prompt
    ExtractMethodDialog.axaml(.cs)   # "Enter new method name" dialog
    CommandPaletteDialog.axaml(.cs)  # Ctrl+Shift+P searchable command list
    SettingsDialog.axaml(.cs)        # Editor/formatting preferences UI
  Styles/
    Colors.axaml          # Color resource dictionary matching STYLEGUIDE.md
    GlueCSharp.xshd        # Custom C# syntax highlighting definition
  docs/
    ROADMAP.md          # Full architecture, feature spec, and phased build order
    STYLEGUIDE.md        # Colors, typography, spacing, component conventions
```

## Roadmap & full spec

The complete architecture, feature breakdown, and phased build order live in [`docs/ROADMAP.md`](docs/ROADMAP.md). This README is the summary — that document is the actual plan.

## Style guide

Colors, typography, spacing, and component conventions — derived from the reference mockup — live in [`docs/STYLEGUIDE.md`](docs/STYLEGUIDE.md).

## Explicitly out of scope

- A visual drag-and-drop UI designer — code-first by choice, not by limitation. Real developers write markup and code directly; dragging boxes around a canvas isn't part of this workflow.

## Project status

**Phase 1 complete. Phase 2 complete.**

Built and working:
- Editor with custom C# syntax highlighting, Explorer sidebar (with a modified-file indicator), File Open/Save/Save As/Save All
- Live Roslyn diagnostics and basic completion (`Ctrl+Space`) — project-aware when a project is loaded, single-file fallback otherwise
- Code folding (`#region`-aware, persisted per file) and Format Document
- **True project parsing** via MSBuildWorkspace (File > Open Project) — real cross-file awareness, not single-file guesswork
- Real `dotnet build` (`Ctrl+Shift+B`) and `dotnet run` (`Ctrl+F5`) as actual subprocesses, with live output
- Go to Definition (`Ctrl+Alt+G`), Find All References (`Ctrl+Alt+R`), Rename (`F2`), Extract Method (`Ctrl+Alt+M`) — genuine Roslyn symbol resolution/data-flow analysis across the whole project
- Multi-file safety: dirty-state tracking, a real Save All, confirmation before Rename writes anything, and unsaved-changes prompts on exit or when switching files
- Integrated terminal (`Ctrl+backtick`) — basic interactive PowerShell shell, plain text I/O
- Command Palette (`Ctrl+Shift+P`) — 28 commands, substring-searchable
- Settings & Preferences (`Ctrl+,`) — editor font/size/indentation, Format on Save, persisted to disk
- Resizable bottom panel (drag the splitter between the editor and Problems/Output/References/Terminal)

Not yet built: multi-tab editing (single-buffer editing for now — see `docs/ROADMAP.md` section 2.2b), and everything from Phase 3 onward (scaffolding/templates, security snippets, debugging, multi-language support, the activity-rail visual restyle, database/Git/secrets integration, the Advisory Layer).

See [`docs/ROADMAP.md`](docs/ROADMAP.md) for the full build order and exactly what's marked done.

## License

All rights reserved. This is a private, personal project. Source is not licensed for reuse, modification, or redistribution.
