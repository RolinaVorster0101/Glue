# Glue — Project Roadmap & Spec

*"Everything your code needs, wired together."*

**Goal:** A personal daily-driver IDE, built to eventually outperform existing tools for your own workflow — multi-language (C#, Go, C, C++, TypeScript, Python, extensible to more), with security and house-style conventions baked in from the start.

---

## 1. Architecture Overview

| Layer | Technology | Notes |
|---|---|---|
| Editor core | **AvalonEdit** | Pure .NET/WPF-native, MIT licensed, forkable — chosen over Monaco to keep the whole codebase in C# and allow deep customization |
| Shell (outer app) | **Avalonia** (or WPF) | Docking panels, solution explorer, output/terminal panes. Avalonia preferred for active community + cross-platform potential |
| C# intelligence | **Roslyn** (`Microsoft.CodeAnalysis.CSharp`) | Hosted in-process. Diagnostics, completion, refactoring, formatting, folding — all from the same compiler VS itself uses |
| Other languages | **LSP (Language Server Protocol)** | Your IDE is an LSP *client*; language servers run as local subprocesses, no network dependency |
| Debugging | **DAP (Debug Adapter Protocol)** | `netcoredbg` (C#), `delve`/`dlv dap` (Go), `lldb-dap`/cpptools adapter (C/C++) |

**Key clarification on dependencies:** LSP and DAP are open specs, not services. Language servers (`gopls` — Google, `clangd` — LLVM, `rust-analyzer` — community) run as local subprocesses, communicating over stdin/stdout. No ongoing connection to Microsoft or anyone else after initial one-time binary download.

**Per-language backend table** (used repeatedly across features below):

| Language | Intelligence source | Formatter | Debugger |
|---|---|---|---|
| C# | Roslyn (in-process) | Roslyn `Formatter.FormatAsync()` + `.editorconfig` | `netcoredbg` |
| Go | `gopls` (LSP) | `gofmt` via `gopls` | `delve` (`dlv dap`) |
| C/C++ | `clangd` (LSP) | `clang-format` via `clangd` | `lldb-dap` / cpptools adapter |
| TypeScript/JavaScript (Node, React) | `typescript-language-server` (LSP) | `prettier` via LSP or standalone | `vscode-js-debug` (DAP-compatible) |
| Python (Phase 5) | `pyright`/`pylsp` (LSP) | via LSP | via DAP |
| CSS/SCSS/LESS | `vscode-css-languageservice`-based LSP server | via LSP | n/a (not a runtime language) |

---

## 2. Feature Set

### 2.1 Editor Core
- Multi-cursor, selection, undo/redo, large-file performance (AvalonEdit baseline)
- Syntax highlighting per language
- **Code folding**
  - C#: Roslyn syntax tree → custom `IFoldingStrategy` (handles `#region`/`#endregion` as first-class trivia, nests correctly)
  - Go/C/C++: LSP `textDocument/foldingRange` → same `IFoldingStrategy` interface, different backend
  - Fallback: AvalonEdit's built-in `BraceFoldingStrategy` for unknown/plain text
  - Python (future): indentation-based folding via LSP; `# region` comment convention as an optional add-on layer
  - **Confirmed:** fold state always persisted per file across sessions — a sidecar store (e.g. `{ "UploadHandler.cs": [120, 340, 502] }`, offsets/line numbers of collapsed regions) restores collapsed state automatically on reopen
  - Plus a global **"Expand All" / "Collapse All" toggle** (button or shortcut) for the current file — overrides/updates the persisted state in one click
- **Format Document / Format Selection** (shortcut, VS-equivalent: Ctrl+K, Ctrl+D / Ctrl+K, Ctrl+F)
  - C#: Roslyn formatter, driven by a **custom `.editorconfig`** encoding your own house style (brace placement, spacing, indentation) rather than defaults
    - *Reminder: an `.editorconfig` is just a small text file that tells the formatter your preferred code style — tabs vs spaces, brace-on-new-line vs same-line, spacing, naming conventions. "Format Document" reads it and conforms your code to it. What you need to decide (whenever convenient): your own personal style preferences for these rules.*
  - Go/C/C++: LSP `textDocument/formatting` / `rangeFormatting` → `gofmt` / `clang-format`

### 2.2 Code Intelligence & Navigation
- Go to Definition / Peek Definition
- Find All References
- Call Hierarchy
- Rename refactoring (safe, project-wide)
- Extract Method / Extract Variable
- Quick Actions / lightbulb-style inline fixes (Roslyn analyzers + code fixes)
- CodeLens-style inline annotations (reference counts, etc.)
- **CSS Quick Actions** (right-click on a selector):
  - "Add Media Query for this selector" → submenu of default breakpoints (Mobile/Tablet/Desktop, values from a personal `breakpoints.json`) or a custom value, inserts a wrapping `@media` block scaffolded around the current rule
  - Same mechanism extends to other scaffolding: `:hover`/`:focus`/`:active` state blocks, dark-mode override (`@media (prefers-color-scheme: dark)`), print stylesheet override
  - Selector-at-cursor detection powered by the CSS LSP server's `hover`/`documentSymbol` requests — no custom CSS parser needed

### 2.3 Project Scaffolding & Templates
- "New Project/Page" wizard: language → project type picker
- Backends:
  - Shell out to native tools where useful (`dotnet new`, `cargo new`/`cargo-generate`)
  - **Custom template engine**: folder of boilerplate files + `template.json` manifest describing placeholder tokens (e.g. `{{ProjectName}}`, `{{Namespace}}`) and file copy rules
- **House-style templates** (the actual differentiator):
  - No Bootstrap, no jQuery, ever — templates built from scratch, not stripped-down Microsoft defaults
  - Your own conventions, defined once and reused everywhere: CSS naming, JS helper patterns (vanilla, no jQuery), comment header format — a personal style, not tied to any single employer's codebase
  - Broad language/framework coverage over time: C#/.NET (Razor Pages, MVC, Blazor), Node.js, React, TypeScript, Rust — general-purpose skill-building, not scoped to one job's tech stack
  - **A correct `.gitignore` is included automatically in every new project**, matched to the language/framework picked in the wizard (e.g. `bin/`/`obj/` for .NET, `node_modules/` for Node/React, `target/` for Rust) — this happens at local scaffolding time (2.3), independent of whether a GitHub repo is created later (2.13). One canonical `.gitignore` per template, kept in the template folder alongside `template.json`, so it never has to be remembered or added by hand.

Example manifest structure:
```
Templates/
  CSharpRazorPage/
    template.json
    Page.cshtml
    Page.cshtml.cs
  ReactTypeScriptComponent/
    template.json
    Component.tsx
  NodeExpressApiStarter/
    template.json
    server.ts
  RustPluginStarter/
    template.json
    Plugin.cs
```

### 2.4 Security-by-Default Snippets
A tagged library of pre-vetted secure code blocks, auto-inserted when a scaffolding scenario matches a known-risky pattern.

```
SecuritySnippets/
  FileUpload/
    ImageUploadHandler.cs
    template.json
  DataAccess/
    ParameterizedQuery.cs
  Forms/
    AntiForgeryToken.cshtml
```

Example — secure image upload, bakes in:
1. File-type validation by content/magic bytes, not extension
2. Server-side re-encoding (strips embedded payloads/malicious metadata)
3. Size limits (form + hard byte cap)
4. Randomized/GUID-based filenames (prevents path traversal/overwrite attacks)
5. Storage outside web-executable paths
6. Explicit `Content-Type`/`Content-Disposition` on serve

Other categories: SQL (parameterized-only), Razor output encoding review flags, CSRF anti-forgery tokens pre-wired into form templates.

### 2.5 Build & Run
- `dotnet build`/MSBuild API integration, captured output pane
- Per-language build detection (Cargo, CMake, `go build`, `.csproj`)
- Launch/run integration

### 2.6 Debugging
- DAP client in the shell
- Breakpoints (including **conditional breakpoints** — break only when an expression is true)
- Watch window / Immediate window (inspect/execute expressions while paused)
- **Hot Reload** (especially valuable for Blazor/UI work)
- Per-language adapters as listed in the backend table above

### 2.7 Solution & Project Management
- Solution Explorer (project/reference-aware file tree)
- NuGet Package Manager UI
- Git integration built into the shell: diff view, commit, branch, blame, merge conflict resolution

### 2.8 Testing
- Test Explorer: discover/run xUnit/NUnit/MSTest tests, inline pass/fail

### 2.9 Productivity Features
- User-definable code snippets (e.g. `ctor` + Tab → constructor skeleton) — smaller-scale sibling of the template system
- Task List: scans for `// TODO`, `// HACK` comments, lists in a panel
- Bookmarks (independent of breakpoints)
- Integrated terminal (embedded shell pane — PowerShell/bash)
- IntelliCode-style AI suggestions (later-stage feature)

### 2.10 Advisory Analysis Layer (Tier 1 + Tier 2)

**Tier 1 — Deterministic, instant, no AI needed**
- Roslyn analyzers/diagnostics (unused variables, null-ref risk, unreachable code) surface issues + one-click Roslyn code fixes (suggested, not forced)
- LSP diagnostics do the same for Go/C/C++ via `gopls`/`clangd`
- Offline, free, no hallucination risk — always the first line of defense

**Tier 2 — LLM-powered advisory layer (explain, don't act)**
- Escalates only what Tier 1 can't catch: logic bugs, non-syntactic security issues, architectural smells
- Triggered manually or passively on save, on a file/selection
- Sends code + surrounding context to the API (reusing the Anthropic-API-in-artifacts pattern)
- Returns **structured findings with an actual fix snippet**, not a silent rewrite of your file:

```json
{
  "issue": "Image upload doesn't validate file content, only extension",
  "why_it_matters": "Attacker can rename a script to .jpg and it'll be accepted",
  "suggested_fix": {
    "explanation": "Check magic bytes before accepting; re-encode via ImageSharp so any embedded payload is stripped",
    "example_code": "byte[] header = new byte[8];\nawait stream.ReadAsync(header, 0, 8);\nif (!IsValidImageSignature(header))\n    throw new InvalidOperationException(\"File is not a valid image.\");\n\nusing var image = Image.Load(stream);\nusing var output = new MemoryStream();\nawait image.SaveAsync(output, new JpegEncoder());"
  },
  "severity": "high",
  "location": "UploadHandler.cs:42"
}
```

- Displayed as an annotation/panel (lightbulb-style), with the fix as reviewable text + code you paste in yourself — never auto-applied
- Trust boundary stays explicit: trivial/safe fixes (formatting, unused imports) may be eligible for auto-apply; anything touching security, data access, or public API surface never is — this boundary is user-defined, not automatic
- Reinforces learning rather than short-circuiting it — fits your first-principles approach to C#/async/memory management

### 2.11 Autonomous Agent Layer (optional, later phase)
Inspired by tools like Google Antigravity — a separate, opt-in orchestration layer on top of everything else:
- Agent(s) take a higher-level task, plan it, write code, run builds/tests, and report back via an "artifact" (task list, file diffs, screenshots)
- Multi-agent workspaces for parallel tasks
- Browser-in-the-loop testing (agent launches the app and verifies UI behavior itself)
- Explicitly distinct from the Advisory Layer above: this *acts*, the Advisory Layer only *explains* — the trust boundary between the two stays visible in the UI, never blurred

### 2.12 Integrated Database Workbench (multi-provider)
Replaces standalone tools like MySQL Workbench — same functionality, integrated into the IDE flow.

- **Abstraction layer**, one interface, swappable providers:
  ```
  IDbProvider
    ├── MySqlProvider      (MySqlConnector)
    ├── PostgresProvider    (Npgsql)
    ├── SqlServerProvider   (Microsoft.Data.SqlClient)
    └── SqliteProvider      (Microsoft.Data.Sqlite)
  ```
  Contract: `GetSchemaTree()`, `ExecuteQuery()`, `GetForeignKeys()`, `Explain()`. MySQL/Postgres/SQL Server use `INFORMATION_SCHEMA`; SQLite uses `sqlite_master` behind the same interface. Build MySQL first — adding others later is a new provider class, not a rearchitecture.
- **Connection manager**: saved connections, test connection, pulled from project config (e.g. `appsettings.json`) automatically where possible
- **Schema browser**: tree view (databases → tables → columns/indexes/foreign keys) via `INFORMATION_SCHEMA`/`sqlite_master` queries
- **Query editor**: another instance of the AvalonEdit core, SQL syntax highlighting, autocomplete fed by schema browser data
- **Results grid**: editable `DataGrid`, generates `UPDATE ... WHERE` on cell edit (needs a primary key to target safely)
- **ER diagrams**: node/edge layout derived from foreign-key metadata
- **EXPLAIN visualization**: structured rendering of query plans
- **Security tie-in**: SQL editor flags/refuses raw string-concatenated queries, reinforcing the same parameterized-query discipline as the security snippet library (2.4)
- **In-flow advantage**: click a table name referenced in code → jump straight to its schema/data, no app-switching

### 2.13 Git & GitHub Integration
- **Local git operations**: **LibGit2Sharp** (.NET-native, no shelling out to git CLI) — stage, commit, branch, diff
- **GitHub operations**: **Octokit.net** (GitHub's official .NET SDK) — create repo, push, pull, PRs, issues
- **Auth**: GitHub OAuth device flow — no pasting personal access tokens into a text box
- **New Repo dialog** (form → Octokit `NewRepository` call):
  ```
  Name, Description, Visibility (public/private),
  ☑ Initialize with README, ☑ Add .gitignore [template ▾], License [▾]
  ```
  ```csharp
  var newRepo = new NewRepository(nameField.Text)
  {
      Description = descriptionField.Text,
      Private = isPrivateRadio.IsChecked,
      AutoInit = initReadmeCheckbox.IsChecked,
      GitignoreTemplate = gitignoreDropdown.SelectedValue,
      LicenseTemplate = licenseDropdown.SelectedValue
  };
  var repo = await githubClient.Repository.Create(newRepo);
  ```
  On success: wire returned clone URL as `origin` via LibGit2Sharp, offer first push — create + connect + push in one flow.
- **Buttons**: Commit (stage + commit), New GitHub Repo (create + wire origin + push), Sync (fetch/push/pull with ahead/behind status; manual conflict-resolution UI for messy cases)
- **Activity strip widget**: a small contributions-heatmap widget, always visible when the panel is open — mirrors GitHub's own contribution calendar
  - Data source: GitHub **GraphQL API v4** (`contributionsCollection.contributionCalendar`), not the REST API — needs `Octokit.GraphQL` (companion package to the REST client), same OAuth token already in use
  - Rendering: 7×~52 grid, cell shade driven by `contributionCount` per day — an Avalonia `ItemsControl`/`Grid` with a count-to-shade value converter, or a quick Visualizer prototype first
  - **Reuse opportunity**: an equivalent widget already exists in KairosNexus — port that GraphQL query + rendering logic over rather than rebuilding from scratch

### 2.14 Secrets Manager (project-scoped, LLM-isolated)
- **Storage**: Windows Credential Manager (DPAPI-encrypted vault), via the `CredentialManagement` NuGet wrapper — never a plaintext file inside the project folder, nothing `git add .` could ever pick up
- **Scoping**: namespaced by whatever project is currently open in the IDE — generic, not tied to any specific project. Key format: `IDE:{ProjectName}:{SecretName}`. No hardcoded list of "known projects" — a new namespace is created the moment a secret is first saved for a project
- **UI**: a "Project Secrets" panel showing masked values for the current project, `[+ Add Secret]` → name + value form → Save
  ```csharp
  var cred = new Credential
  {
      Target = $"IDE:{projectName}:{secretName}",
      Username = secretName,
      Password = secretValue,
      PersistanceType = PersistanceType.LocalComputer
  };
  cred.Save();
  ```
- **Runtime access**: `SecretsManager.Get(projectName, "SecretName")` — reads via the same key, returns plaintext only in memory to the calling code (DB providers, Git/GitHub auth, etc.)
- **Architectural isolation from the LLM Advisory Layer (2.10)**: `SecretsManager` is never referenced by the Advisory Layer's code path — no import, no dependency, no object-graph connection. The LLM literally cannot reach secret values because nothing wires it to the module that holds them.
- **Tier 1 linting nudge** (deterministic, no AI, same category as SQL-injection checks in 2.4): flag any string literal that looks like a hardcoded connection string, API key, or token — "use SecretsManager instead" — catching the mistake at write-time, before it's ever a `.gitignore` problem or an Advisory Layer exposure risk
- **Optional convenience**: right-click in editor → "Insert secret reference" → auto-writes `SecretsManager.Get(ProjectName, "SecretName")` at the cursor, removing the temptation to type raw values even out of habit

### 2.15 Menus, Command Palette & Settings
Standard IDE-shell basics that every other feature above assumes exist, but hadn't been made explicit.

**File menu**
- New File / New Project (routes into the scaffolding wizard, 2.3)
- Open / Open Recent (recently opened files/folders)
- Save / Save As / Save All / Auto Save toggle
- Close Editor / Close Folder
- Revert File (discard unsaved changes back to last-saved state)
- Exit

**Edit menu**
- Undo / Redo
- Find / Find & Replace (in-file), Find in Files (project-wide)
- Line operations: move line up/down, duplicate line, toggle line comment
- Multi-cursor commands: add cursor above/below, select all occurrences

**View menu**
- Toggle Sidebar / Toggle Panel (terminal/output/problems) / Toggle Minimap
- Zoom in/out
- Split Editor (side-by-side files)
- Full Screen
- Command Palette (see below)

**Command Palette** (the important one)
A fuzzy-searchable, keyboard-triggered (e.g. `Ctrl+Shift+P`) list of every command in the IDE — Format Document, New Repo, Add Secret, Run Advisory Check, everything. As the feature surface grows across menus/panels/shortcuts, this becomes the fastest way to reach anything without memorizing where it lives. Should be wired up as commands are built, not bolted on at the end — every new feature registers itself here as it's added.

**Tools/Preferences menu → Settings & Preferences page**
A single place to manage the IDE itself, rather than hand-editing config files:
- **Editor**: font/size, theme, tab size, default fold-state behavior, keybinding for Expand/Collapse All
- **Formatting**: view/edit your `.editorconfig` house style directly from the UI
- **CSS Quick Actions**: personal `breakpoints.json` defaults (Mobile/Tablet/Desktop values)
- **Language servers**: configure paths to installed LSP/DAP binaries (`gopls`, `clangd`, `pyright`, etc.) once Phase 5 lands
- **Templates**: default template folder location, manage/edit scaffolding templates
- **Advisory Layer**: toggle Tier 2 on/off, choose model, define the auto-apply trust boundary
- **Git/GitHub**: default author name/email, OAuth connection status
- **Keyboard Shortcuts**: rebind any command
- **Color Theme**: picker (dark/light/custom)
- **Secrets**: link straight into the per-project Secrets panel (not editable values here)

**Help menu**
- About, Documentation link
- **Check for Updates — manual and opt-in only.** Consistent with the "no forced updates, no surprise policy popups" principle: Glue never auto-updates or nags about a new version. Checking is something you trigger, never something that interrupts you.


### Explicitly excluded
- **Visual drag-and-drop UI designer** — deliberately excluded, not a limitation. Code-first by choice: writing markup/XAML directly gives more control and speed than dragging elements on a canvas.

---

## 3. Suggested Build Order

**Phase 1 — Core loop**
1. Shell app + AvalonEdit showing a single file, syntax highlighted
2. Solution/project parsing (open folder, list files, open `.csproj`)
3. Roslyn wired in: live diagnostics, basic completion
4. Format Document (Roslyn formatter + your `.editorconfig`)
5. Code folding (`#region`-aware, Roslyn-backed) — persisted per file, plus Expand All/Collapse All toggle
6. Basic File/Edit/View menu shell: New/Open/Save/Save As/Save All, Undo/Redo, Find & Replace, line operations, Toggle Sidebar/Panel

**Phase 2 — Build & navigate**
7. Build integration (`dotnet build` via Process/MSBuild API) + output pane
8. Run/launch integration
9. Go to Definition, Find References, Rename, Extract Method (Roslyn-backed)
10. Integrated terminal
11. Command Palette — every command from this phase onward registers itself here as it's built
12. Settings & Preferences page (editor, formatting, keybindings, theme — expands as later phases add more categories)

**Phase 3 — Scaffolding & security**
13. Template manifest format + wizard UI
14. First custom templates (no Bootstrap/jQuery, house-style conventions)
15. Security snippet library (starting with secure image upload)

**Phase 4 — Debugging**
16. DAP client + `netcoredbg` integration for C#
17. Conditional breakpoints, Watch/Immediate window
18. Hot Reload

**Phase 5 — Multi-language**
19. LSP client integration (`OmniSharp.Extensions.LanguageServer` as base)
20. Go support via `gopls` (intelligence, folding, formatting, DAP via `delve`)
21. C/C++ support via `clangd` (same pattern, DAP via `lldb-dap`/cpptools)
22. Python support via `pyright`/`pylsp` (DAP via debugpy)
23. CSS/SCSS/LESS support via `vscode-css-languageservice`-based LSP server, enabling CSS Quick Actions (media queries, pseudo-states, dark mode)

**Phase 6 — Polish & extras**
24. **Restyle shell to activity-rail layout per docs/STYLEGUIDE.md** — replace the classic File/Edit/View menu bar with an icon-only activity rail (Explorer, Search, Git, Run, Extensions, etc.), top bar with breadcrumbs/search/action icons, and panels that swap based on the selected activity icon, matching the reference mockup. Phase 1–5 build functionality first with plain menus deliberately, to avoid fighting layout and logic simultaneously — this is the single focused visual pass once most functionality exists, rather than restyling piecemeal as each feature lands.
25. Solution Explorer refinement, NuGet UI, Git integration
26. Test Explorer
27. Snippets, Task List, Bookmarks
28. Persisted fold state, themes, further UX polish
29. Full menu/shell polish: Open Recent, Auto Save, Multi-cursor commands, Split Editor, Zoom, Full Screen
30. Keyboard Shortcuts editor, Color Theme picker, Extensions manager (Settings page expansion)

**Phase 7 — Advisory Analysis Layer**
31. Tier 1: surface existing Roslyn/LSP diagnostics in a unified panel
32. Tier 2: LLM advisory call (file + context → structured findings with `example_code`, not silent rewrites)
33. UI for reviewable, opt-in fix application per finding

**Phase 8 — Database, Git/GitHub, and Secrets**
34. `IDbProvider` abstraction + MySQL provider first (schema browser, query editor, results grid)
35. ER diagrams, EXPLAIN visualization, additional providers (Postgres/SQL Server/SQLite) as needed
36. LibGit2Sharp integration: commit/stage/branch/diff buttons
37. Octokit integration: New Repo dialog, OAuth device flow, Sync button
38. Secrets Manager: Credential Manager storage, project-scoped panel, Tier 1 hardcoded-secret linting rule, LLM-isolation boundary verified (no reference from Advisory Layer code)

**Phase 9 — Autonomous Agent Layer (optional, last)**
39. Agent task runner: plan → write → build/test → report via artifact
40. Multi-agent workspaces for parallel tasks
41. Browser-in-the-loop verification

---

## 4. Open Decisions to Revisit
- **Confirmed:** fold state always persisted per file, plus an Expand All/Collapse All toggle for the current file
- `.editorconfig` house style: still open — reminder that this file is what tells the formatter your preferred code style (brace placement, tabs vs spaces, spacing, naming conventions); to be defined whenever convenient, not urgent
- Python support: **confirmed for Phase 5**, alongside Go/C/C++ via LSP
- General-purpose scope confirmed: templates/scaffolding should span C#/.NET (Razor Pages, MVC, Blazor), Node.js, React, TypeScript, and Rust — a broad personal skill-building tool, not scoped to any single employer's codebase or conventions
