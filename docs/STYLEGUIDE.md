# Glue — Style Guide

---

## 1. Overall aesthetic

Dark-first, flat, high information density, one accent color doing all the "this is interactive/active" work. No gradients, no drop shadows except very subtle card separation, no decorative chrome. Everything reads as calm and technical — the interface gets out of the way of the code.

---

## 2. Color palette

### Base surfaces (darkest → lightest)
| Token | Approx. hex | Used for |
|---|---|---|
| `--bg-canvas` | `#0D1117` | Outermost app background, code editor background |
| `--bg-surface` | `#12161C` | Sidebars, top bar, tab bar, bottom panel — one step up from canvas |
| `--bg-surface-raised` | `#161B22` | Cards (Advisory suggestion card, System Monitor card), hover states |
| `--bg-active-tab` | `#1C2128` | The currently active editor tab, distinguished from inactive tabs |

### Text
| Token | Approx. hex | Used for |
|---|---|---|
| `--text-primary` | `#E6EDF3` | File names, main body text, active tab label |
| `--text-secondary` | `#8B949E` | Breadcrumbs, inactive tab labels, section labels, line numbers |
| `--text-muted` | `#6E7681` | Placeholder text, disabled items |

### Accent (the one color that means "interactive / active / brand")
| Token | Approx. hex | Used for |
|---|---|---|
| `--accent` | `#4F8EF7` | Logo mark, active sidebar icon, active tab top border, Run button, links, GIT branch icon, primary buttons (Apply), status bar background |
| `--accent-hover` | `#6BA0F8` | Hover state on accent elements |

### Semantic status colors
| Token | Approx. hex | Used for |
|---|---|---|
| `--success` | `#3FB950` | Success text, "Build succeeded", clean-file dot, RAM/DISK gauge rings, added-file badges |
| `--warning` | `#D29922` | Warning triangle icons, modified-folder dot, CPU gauge ring |
| `--danger` | `#F85149` | Error circle icons, error counts |
| `--info` | `#58A6FF` | Info circle icons (same family as accent, slightly desaturated) |

### Syntax highlighting (code editor)
| Token | Approx. hex | Used for |
|---|---|---|
| `--syntax-keyword` | `#C792EA` (purple) | `using`, `public`, `namespace`, `class`, `async`, `await`, `var` |
| `--syntax-type` | `#4EC9B0` (teal) | Class/interface names — `JobController`, `IJobService` |
| `--syntax-string` | `#CE9178` (tan/orange) | String literals |
| `--syntax-method` | `#DCDCAA` (soft gold) | Method names |
| `--syntax-comment` | `#6A9955` (muted green) | Comments |
| `--syntax-attribute` | `#D4D48A` | Attribute brackets, e.g. `[ApiController]` |

### File-type icon colors
| Color | Used for |
|---|---|
| Teal/cyan | C# file icon ("C#" mark) |
| Blue | Folder icons |
| Amber/orange | JSON file icon (`{ }` mark) |

---

## 3. Typography

| Role | Typeface | Size | Weight | Notes |
|---|---|---|---|---|
| UI chrome (menus, breadcrumbs, tabs, sidebar tree) | Inter / Segoe UI / system sans | 13px | 400–500 | Same family throughout the shell — no serif anywhere |
| Section labels (`EXPLORER`, `GIT`, `SYSTEM MONITOR`) | Same sans | 11px | 600 | Uppercase, letter-spacing ~0.5px, `--text-secondary` |
| Code editor | Cascadia Code / JetBrains Mono / Consolas | 13–14px | 400 | Monospace, generous line-height (~1.6) for readability |
| Status bar | Same sans | 11–12px | 400 | Compact, single line, white text on accent-blue background |
| Panel titles (tab labels: Terminal, Problems, Output) | Same sans | 12px | 500 | Active tab in `--text-primary`, inactive in `--text-secondary` |

**Two weights only in practice**: 400 (regular) for body/code, 500–600 (medium/semibold) for labels, active states, and section headers. Nothing heavier.

---

## 4. Layout & spacing

| Region | Approx. size | Notes |
|---|---|---|
| Top bar | 48–56px height | Logo + wordmark, breadcrumb trail, centered search, right-aligned action icons + avatar |
| Activity rail (far left) | 44–48px wide | Icon-only, one icon per major panel (Explorer, Search, Git, Run, Grid, Docs, Charts), generous vertical gaps (~20–24px between icons), Settings + Avatar pinned to the bottom |
| Explorer / side panel | 220–260px wide | Section header, "Open Editors" list, then project tree with folder/file icons and status dots |
| Tab bar | 36–40px height | One row, file icon + name + modified indicator + close × per tab |
| Code editor | Fills remaining space | Line numbers column (right-aligned, muted), minimap docked far right (~50–60px wide) |
| Bottom panel | 180–200px height | Tabbed: Terminal / Problems / Output / Debug Console / Test Results — Terminal and Problems often shown side by side |
| Right panel (Advisory/Git/System Monitor) | 280–300px wide | Stacked cards: suggestion card, Git changes card, System Monitor card with circular gauges + line chart |
| Status bar | 28–32px height | Full-width, solid accent-blue background, white text, left-aligned git/error info, right-aligned cursor position/encoding/language |

### Spacing scale
- Micro (icon-to-label gaps, inline badges): 4–6px
- Small (list item padding, tab padding): 8–12px
- Medium (card padding, panel padding): 12–16px
- Section gaps (between stacked cards in the right panel): 16–20px

### Borders & separation
- All dividers are 1px hairline, low-contrast (`--bg-surface-raised` against `--bg-surface`, not a bright line) — separation comes from subtle background-shade steps, not strong borders
- Corner radius: 6–8px on cards and buttons; tabs are square or very slightly rounded at the top only; the accent-color "active tab" indicator is a 2px top border, not a rounded highlight

---

## 5. Iconography

- Outline-style icons throughout (not filled), consistent stroke weight
- Icon size: 16–20px inline in menus/lists, 20px in the activity rail
- Status dots (small circles next to folder/file names): green = clean, amber/orange = modified, used sparingly — not every row, only where state matters
- Colored letter badges (`M`, `A`) next to changed files in the Git panel — single-letter, colored by change type (modified/added), same visual language as the status dots

---

## 6. Component notes

**Cards** (Advisory suggestion, System Monitor): `--bg-surface-raised` background, 1px hairline border, 8px radius, 12–16px padding, no shadow — separation from the panel background alone is enough.

**Buttons**: primary action (Apply) filled with `--accent`, white text; secondary action (Dismiss) outline-only, transparent background, `--text-secondary` text, hover to `--bg-surface-raised`.

**Circular gauges** (CPU/RAM/DISK): thin ring (not filled pie), semantic color per metric or per threshold (e.g. green = healthy, amber = elevated), percentage centered inside in `--text-primary`, label below in `--text-secondary`.

**Line chart** (System Monitor): two thin lines (blue + green), no fill/area under the line, flat background, minimal axis labels — small timestamps along the bottom only.

**Status bar**: the one place solid `--accent` fills an entire bar rather than being used as a small highlight — this is deliberate, it's the "always visible" anchor of the whole shell.

---

## 7. What to carry forward vs. adapt

- **Carry forward as-is**: the dark base palette, the single-accent-color discipline, the flat/no-gradient rule, the compact information density, the outline iconography
- **Adapt for Glue specifically**: the "IDE ASSISTANT BETA" panel framing maps onto Glue's actual Advisory Layer (Tier 1/Tier 2 findings, per the roadmap)
- **Not in scope**: the System Monitor widget (CPU/RAM/DISK gauges) in the reference image was used purely for its color/look-and-feel — it is not a planned Glue feature. Only what's already in `docs/ROADMAP.md` is being built.
