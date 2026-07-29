# Glue — Project Roadmap & Spec

*"Everything your code needs, wired together."*

**Goal:** A personal daily-driver IDE, built to eventually outperform existing tools for your own workflow — multi-language (C#, Go, C, C++, TypeScript, Python, extensible to more), with security and house-style conventions baked in from the start.

---

## 1. Architecture Overview

| Layer | Technology | Notes |
|---|---|---|
| Editor core | **AvaloniaEdit** | Avalonia's port of AvalonEdit — a DIFFERENT NuGet package (`Avalonia.AvaloniaEdit`) from WPF's AvalonEdit, easy to mix up. MIT licensed, forkable. |
| Shell (outer app) | **Avalonia** (.NET 8) — decided, not "or WPF" | Cross-platform, active community. Currently a classic File/Edit/View/Build menu bar (Phase 1–5 deliberately build function first); the activity-rail visual restyle from `docs/STYLEGUIDE.md` is explicitly Phase 6. |
| C# intelligence | **Roslyn** (`Microsoft.CodeAnalysis.CSharp` + `.Workspaces` + `.Features` + `.CSharp.Features`) | Hosted in-process. Diagnostics, completion, formatting, folding, navigation (Go to Definition/Find References/Rename) — all from the same compiler VS itself uses. |
| True project parsing | **MSBuildWorkspace** (`Microsoft.CodeAnalysis.Workspaces.MSBuild` + `Microsoft.Build.Locator`) | Loads a real `.csproj` — all its files, references, target framework. `MSBuildLocator.RegisterDefaults()` must run as the literal first line of `Main()`, before anything else touches MSBuild/Roslyn.MSBuild types. |
| Other languages | **LSP (Language Server Protocol)** | Your IDE is an LSP *client*; language servers run as local subprocesses, no network dependency. Not yet built — Phase 5. |
| Debugging | **DAP (Debug Adapter Protocol)** | `netcoredbg` (C#), `delve`/`dlv dap` (Go), `lldb-dap`/cpptools adapter (C/C++). Not yet built — Phase 4. |
| Dev tooling | **Avalonia.Diagnostics (DevTools)** | Debug-config only, `F12` to open. Used repeatedly to diagnose real Avalonia style-priority/template bugs rather than guessing blind. `F12` is reserved for DevTools — navigation features use `Ctrl+Alt+G`/`Ctrl+Alt+R`/`F2` instead to avoid the conflict. |

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
- **✅ Built:** AvaloniaEdit-based editor, custom C# syntax highlighting matching `docs/STYLEGUIDE.md` (`GlueCSharp.xshd`), File Open/Save/Save As, Explorer sidebar (folder tree)
- **✅ Built:** Basic completion (`Ctrl+Space`) — Roslyn's `CompletionService`, project-aware when a project is loaded (sees other files' types/members), single-file fallback otherwise
- Multi-cursor, selection — not yet built (undo/redo and large-file performance come from AvaloniaEdit's own baseline, already functional)
- **Code folding — ✅ Built**
  - C#: Roslyn syntax tree → custom folding logic (`RoslynFoldingService`), handles `#region`/`#endregion` (nesting, label text, indentation) and class/method/constructor/property bodies
  - Go/C/C++: LSP `textDocument/foldingRange` — not yet built (Phase 5)
  - Fold state persisted per file (`FoldStateStore`, JSON sidecar under `%APPDATA%\Glue\foldstate.json`), restored on reopen
  - Expand All / Collapse All in the View menu
- **Format Document — ✅ Built** (`Ctrl+Alt+F` placeholder shortcut)
  - C#: Roslyn's formatter, currently using its **default conventions** — the personal `.editorconfig` house style is still an open decision (not urgent)
  - Go/C/C++: LSP-based — not yet built (Phase 5)

### 2.2 Code Intelligence & Navigation
- **✅ Built:** Go to Definition (`Ctrl+Alt+G`) / Find All References (`Ctrl+Alt+R`) / Rename (`F2`) — all via Roslyn's `SymbolFinder`/`Renamer` against the loaded project (`RoslynProjectService`/`MSBuildWorkspace`), not single-file guesswork. Rename shows a confirmation dialog listing every affected file before anything is written; changes to files other than the one currently open are held as **pending changes** (not written to disk) until Save All — see "2.2a Multi-file safety" below.
- Peek Definition (inline, without leaving the file) — not yet built, Go to Definition currently always jumps/switches files
- Call Hierarchy
- **✅ Built:** Extract Method (`Ctrl+Alt+M`) — no equivalent public Roslyn API to Go to Definition/Find References/Rename's `SymbolFinder`/`Renamer`, so this one is hand-rolled using `SemanticModel.AnalyzeDataFlow` to infer parameters (variables flowing in) and return value (a single variable flowing out, if any). Scoped narrow: whole statements only, single method, 0-1 output variables (multiple would need a tuple return or out params, not attempted). Extract Variable — not yet built.
- Quick Actions / lightbulb-style inline fixes (Roslyn analyzers + code fixes)
- CodeLens-style inline annotations (reference counts, etc.)
- **CSS Quick Actions** (right-click on a selector):
  - "Add Media Query for this selector" → submenu of default breakpoints (Mobile/Tablet/Desktop, values from a personal `breakpoints.json`) or a custom value, inserts a wrapping `@media` block scaffolded around the current rule
  - Same mechanism extends to other scaffolding: `:hover`/`:focus`/`:active` state blocks, dark-mode override (`@media (prefers-color-scheme: dark)`), print stylesheet override
  - Selector-at-cursor detection powered by the CSS LSP server's `hover`/`documentSymbol` requests — no custom CSS parser needed

### 2.2a Multi-file safety (built, not originally itemized — added once Rename made it necessary)
Rename can affect files other than the one currently open, which exposed a real gap: Glue only edits one file at a time, so there was no "unsaved" concept for files it never actually opened. Fixed with:
- **Dirty-state tracking**: a `_lastSavedText` snapshot (what's actually on disk) compared against live editor content — the same pattern that will generalize to per-tab tracking once multi-tab editing (see 2.2b) lands.
- **Pending file changes**: a `_pendingFileChanges` dictionary (file path → new content) for Rename-affected files that aren't currently open. Opening one of these files surfaces its pending content instead of the stale on-disk version.
- **Save All**: genuinely saves the current file (if dirty) *and* every pending file, then reloads the project so Roslyn's symbol table reflects reality. Distinct from plain Save, which only ever touches the currently open file.
- **Confirmation before writing**: Rename shows every affected file and requires explicit confirmation before touching anything — nothing is written just by choosing a new name.
- **Unsaved-changes prompts**: closing the window, using File > Exit, or switching to a different file (Open File, Explorer click, Go to Definition, Find All References — all funnel through one shared `LoadFileIntoEditorAsync`) all check for unsaved changes first and offer Save All / Discard / Cancel via a shared dialog.

### 2.2b Multi-tab editing (planned, not yet built)
Glue currently edits one file at a time — opening a new file replaces the current buffer (with the unsaved-changes prompt above if it's dirty). Multiple simultaneously open files (tabs across the top, matching the reference mockup and every mainstream IDE) is planned. When built, the per-file dirty-tracking pattern already established (2.2a) generalizes directly — each tab gets its own instance of that same tracking, rather than the single global one used today. Not yet assigned to a specific phase; likely alongside Split Editor in Phase 6, since both concern the same "more than one file open/visible at once" architecture.

### 2.3 Project Scaffolding & Templates
- "New Project/Page" wizard: language → project type picker
- Backends:
  - Shell out to native tools where useful (`dotnet new`, `cargo new`/`cargo-generate`)
  - **Custom template engine**: folder of boilerplate files + `template.json` manifest describing placeholder tokens (e.g. `{{ProjectName}}`, `{{Namespace}}`) and file copy rules
- **House-style templates** (the actual differentiator):
  - No Bootstrap, no jQuery, ever — templates built from scratch, not stripped-down Microsoft defaults
  - **Every template that ships CSS uses BEM naming** (`block__element--modifier`) — a short guideline comment explaining the convention lives at the top of the stylesheet itself (below the table of contents), not just in this doc, so anyone else working on the generated project sees it in context. Applies going forward to every CSS-shipping template (Razor Pages ✅, MVC, Blazor, React, etc.), not just the first one it was introduced on.
  - CSS files also use a table-of-contents header + `#region`/`#endregion` comment markers grouping logical sections (Design Tokens, Base/Reset, Layout, Navigation, etc.) — same convention across templates
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

**Full template list** (built incrementally — see checkmarks; the actual mechanism is `Services/ProjectScaffoldingService.cs`, expressed as C# records rather than a separate JSON manifest file for now, see that file's doc comment). Deliberate pacing: a couple of C#/.NET web templates come next since they're the ones where the "no Bootstrap/jQuery" house-style principle is actually visible, not just an abstract goal — the rest of this list gets picked up incrementally between other phases, not ground through all at once (scaffolding is comparatively low-risk/low-complexity work, a reasonable fill-in task between bigger features like the Advisory Layer or debugging).

*C#/.NET:*
- ✅ Console App
- ✅ Class Library
- ASP.NET Core Razor Pages
- ASP.NET Core MVC
- **ASP.NET Core MVC + Razor Pages (Hybrid)** — both conventions in one project (`AddControllersWithViews()` + `AddRazorPages()`, `Controllers/`+`Views/` alongside `Pages/`, one shared `_Layout.cshtml`) — a genuinely common real-world pattern, not an edge case
- Blazor Server
- Blazor WebAssembly (distinct hosting model from Server, own template)
- ASP.NET Core Web API (controller-based)
- Minimal API (endpoint-mapping style, no controllers)
- Worker Service (long-running background service, no web front-end)
- gRPC Service
- xUnit Test Project
- Avalonia Desktop App (a bit meta — Glue's own stack — but useful for scaffolding other desktop tools the same way)

*Node.js / TypeScript / React:*
- Node.js + Express API starter (TypeScript)
- React + TypeScript component/app starter (no Bootstrap, own styling approach)
- Vue.js + TypeScript starter

*Python* (project scaffolding itself doesn't need Phase 5's LSP work — creating files is independent of language intelligence; a `.py` file just won't get live diagnostics/completion/navigation in Glue until `gopls`-equivalent Python support lands):
- Plain Python script/project starter
- FastAPI starter (matches the Fathom project's stack)

*Rust:*
- Rust Oxide/uMod plugin starter (directly useful for "From Dust to Rust ZA")

*Go* (same LSP caveat as Python above):
- Go CLI/module starter

*Other web:*
- Static HTML/CSS/JS site (no framework)

*Once 2.4 (security snippets) exists:*
- ASP.NET Core Razor Page with secure file upload pre-wired — demonstrates "security by default" in a real template, not just the abstract principle

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
- **✅ Built:** `dotnet build` as a real subprocess (`BuildService`), streamed live into an Output tab, `Ctrl+Shift+B` — surfaces genuine MSBuild/NuGet errors, not just Roslyn's in-process diagnostics. Fixed a real bug along the way: `MSBuildLocator.RegisterDefaults()` (needed for `MSBuildWorkspace`, see 2.2's project parsing) sets environment variables that leak into child `dotnet` processes and cause an assembly-version conflict (`MSB4018`) — stripped before spawning the build/run subprocess.
- Per-language build detection (Cargo, CMake, `go build`) — not yet built (Phase 5)
- **✅ Built:** `dotnet run` as a subprocess (`RunService`), live output, cancellable (`Ctrl+F5` / Stop) — plain run-without-debugging, no debugger attached yet (that's Phase 4)

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
- **✅ Built:** Integrated terminal (`Ctrl+backtick`) — basic interactive shell (persistent PowerShell subprocess, plain text in/out via `TerminalService`). Deliberately not a full terminal emulator: no ANSI colors, no cursor repositioning, no full-screen apps (vim, htop). Bottom panel (this + Problems/Output/References) is resizable via a draggable `GridSplitter`.
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

*(Multi-tab editing — see section 2.2b for the full note; likely lands alongside Split Editor above, since both concern "more than one file open/visible at once.")*

**Command Palette — ✅ Built** (`Ctrl+Shift+P`)
Substring-filtered (not true fuzzy matching — a simpler, safer first pass), 28 commands currently registered covering every menu item in the app. Arrow keys navigate, Enter runs the selected command, Escape cancels. New commands get added to `BuildCommandPaletteItems()` in `MainWindow.axaml.cs` as they're built, per the original design intent below.

**Tools/Preferences menu → Settings & Preferences page — ✅ Built, scoped** (`Ctrl+,`)
The original design (below) described a much bigger page than what's actually buildable today — most categories don't have real functionality behind them yet. What's actually built and functional:
- **Editor**: font family, font size, indentation size, convert-tabs-to-spaces — all take effect immediately, persisted to `%APPDATA%\Glue\settings.json`
- **Formatting**: Format Document automatically on Save (toggle)

Everything else below is the **original full design intent**, to be filled in as those features actually land — not placeholder settings for things that don't exist:
- **Editor** (remaining): theme, default fold-state behavior, keybinding for Expand/Collapse All
- **Formatting** (remaining): view/edit your `.editorconfig` house style directly from the UI
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
1. ✅ Shell app + AvaloniaEdit showing a single file, syntax highlighted
2. ✅ Solution/project parsing (open folder, list files, open `.csproj`) — via MSBuildWorkspace, genuinely loads the whole project
3. ✅ Roslyn wired in: live diagnostics, basic completion — both project-aware when a project is loaded, single-file fallback otherwise
4. ✅ Format Document (Roslyn formatter — currently default conventions; your `.editorconfig` house style is still an open decision)
5. ✅ Code folding (`#region`-aware, Roslyn-backed) — persisted per file, plus Expand All/Collapse All toggle
6. ✅ Basic File/Edit/View menu shell: New/Open/Save/Save As/Save All, Undo/Redo, Find (Replace still missing — AvaloniaEdit's stock SearchPanel is find-only), line operations, Toggle Sidebar/Panel

**Phase 2 — Build & navigate**
7. ✅ Build integration — real `dotnet build` subprocess, live Output tab, `Ctrl+Shift+B`
8. ✅ Run/launch integration — real `dotnet run` subprocess, live output, cancellable, `Ctrl+F5`
9. ✅ Go to Definition (`Ctrl+Alt+G`), Find All References (`Ctrl+Alt+R`), Rename (`F2`, with confirmation + pending-changes safety — see 2.2a), Extract Method (`Ctrl+Alt+M`, via `SemanticModel.AnalyzeDataFlow` — hand-rolled, no public Roslyn API for this one unlike the others; scoped to whole-statement selections in a single method, 0-1 output variables)
10. ✅ Integrated terminal (`Ctrl+backtick`) — basic interactive shell (persistent PowerShell subprocess, plain text I/O; NOT a full terminal emulator — no ANSI colors, no full-screen apps)
11. ✅ Command Palette (`Ctrl+Shift+P`) — substring-filtered (not true fuzzy matching), 28 commands registered
12. ✅ Settings & Preferences page (`Ctrl+,`) — scoped to what's actually functional today: editor font/size/indentation, Format on Save; persisted to `%APPDATA%\Glue\settings.json`. The bigger Settings page described earlier in this doc (theme, LSP paths, Advisory Layer, Git/GitHub, Secrets) will grow into this as those features actually get built — no placeholder settings for things that don't exist yet.

**Phase 2 is now fully complete.** Also added along the way, not originally itemized: a resizable bottom panel (`GridSplitter` between editor and Problems/Output/References/Terminal, replacing a fixed height), and a modified-file indicator in the Explorer tree (subtle gold/tan text color, matching the syntax highlighter's Method color).

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
